import { useEffect, type RefObject } from 'react';

export function useOrb(
  canvasRef: RefObject<HTMLCanvasElement | null>,
  stateRef: RefObject<{ screen: string; listening: boolean; speaking: boolean }>
) {
  useEffect(() => {
    const cv = canvasRef.current;
    if (!cv) return;

    let gl: WebGLRenderingContext | null = null;
    let program: WebGLProgram | null = null;
    let animId = 0;

    // Try WebGL first
    try {
      gl = cv.getContext('webgl', { alpha: true, antialias: true, powerPreference: 'high-performance' })
        || (cv.getContext('experimental-webgl') as WebGLRenderingContext | null);
    } catch {
      gl = null;
    }

    // State tracking with smooth damping
    const mouse = { x: 0, y: 0, targetX: 0, targetY: 0 };
    let currentAmp = 0.2;
    let targetAmp = 0.2;
    let startTime = performance.now();

    const onMouseMove = (e: MouseEvent) => {
      mouse.targetX = (e.clientX / window.innerWidth - 0.5) * 2;
      mouse.targetY = -(e.clientY / window.innerHeight - 0.5) * 2;
    };
    window.addEventListener('mousemove', onMouseMove, { passive: true });

    // WebGL Shaders
    const vsSource = `
      attribute vec2 a_pos;
      varying vec2 v_uv;
      void main() {
        v_uv = (a_pos + 1.0) * 0.5;
        gl_Position = vec4(a_pos, 0.0, 1.0);
      }
    `;

    const fsSource = `
      precision highp float;
      varying vec2 v_uv;
      uniform vec2 u_res;
      uniform float u_time;
      uniform vec2 u_mouse;
      uniform float u_amp;
      uniform float u_state; // 0=idle, 1=listen, 2=speak
      uniform vec2 u_center;
      uniform float u_radius;

      // 3D Simplex-like noise helper
      vec3 mod289(vec3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
      vec4 mod289(vec4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
      vec4 permute(vec4 x) { return mod289(((x*34.0)+1.0)*x); }
      vec4 taylorInvSqrt(vec4 r) { return 1.79284291400159 - 0.85373472095314 * r; }

      float snoise(vec3 v) {
        const vec2 C = vec2(1.0/6.0, 1.0/3.0);
        const vec4 D = vec4(0.0, 0.5, 1.0, 2.0);
        vec3 i  = floor(v + dot(v, C.yyy));
        vec3 x0 = v - i + dot(i, C.xxx);
        vec3 g = step(x0.yzx, x0.xyz);
        vec3 l = 1.0 - g;
        vec3 i1 = min(g.xyz, l.zxy);
        vec3 i2 = max(g.xyz, l.zxy);
        vec3 x1 = x0 - i1 + C.xxx;
        vec3 x2 = x0 - i2 + C.yyy;
        vec3 x3 = x0 - D.yyy;
        i = mod289(i);
        vec4 p = permute(permute(permute(
                  i.z + vec4(0.0, i1.z, i2.z, 1.0))
                + i.y + vec4(0.0, i1.y, i2.y, 1.0))
                + i.x + vec4(0.0, i1.x, i2.x, 1.0));
        float n_ = 0.142857142857;
        vec3  ns = n_ * D.wyz - D.xzx;
        vec4 j = p - 49.0 * floor(p * ns.z * ns.z);
        vec4 x_ = floor(j * ns.z);
        vec4 y_ = floor(j - 7.0 * x_);
        vec4 x = x_ *ns.x + ns.yyyy;
        vec4 y = y_ *ns.x + ns.yyyy;
        vec4 h = 1.0 - abs(x) - abs(y);
        vec4 b0 = vec4(x.xy, y.xy);
        vec4 b1 = vec4(x.zw, y.zw);
        vec4 s0 = floor(b0)*2.0 + 1.0;
        vec4 s1 = floor(b1)*2.0 + 1.0;
        vec4 sh = -step(h, vec4(0.0));
        vec4 a0 = b0.xzyw + s0.xzyw*sh.xxyy;
        vec4 a1 = b1.xzyw + s1.xzyw*sh.zzww;
        vec3 p0 = vec3(a0.xy, h.x);
        vec3 p1 = vec3(a0.zw, h.y);
        vec3 p2 = vec3(a1.xy, h.z);
        vec3 p3 = vec3(a1.zw, h.w);
        vec4 norm = taylorInvSqrt(vec4(dot(p0,p0), dot(p1,p1), dot(p2, p2), dot(p3,p3)));
        p0 *= norm.x; p1 *= norm.y; p2 *= norm.z; p3 *= norm.w;
        vec4 m = max(0.6 - vec4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3)), 0.0);
        m = m * m;
        return 42.0 * dot(m*m, vec4(dot(p0,x0), dot(p1,x1), dot(p2,x2), dot(p3,x3)));
      }

      // Chromatic Palette Function
      vec3 iridescence(float t, vec3 a, vec3 b, vec3 c, vec3 d) {
        return a + b * cos(6.28318 * (c * t + d));
      }

      void main() {
        // Pixel aspect correction
        vec2 st = (gl_FragCoord.xy - u_center) / u_res.y;
        float rMax = u_radius / u_res.y;

        // Mouse tilt
        vec2 mOffset = u_mouse * 0.08;
        vec2 p = st - mOffset * 0.4;

        float dist = length(p);

        // Background dark gradient with subtle deep space vignette
        vec3 finalColor = vec3(0.024, 0.031, 0.047); // #06080d

        // Soft ground chromatic reflection below the sphere
        vec2 shadowUV = st - vec2(0.0, -rMax * 1.15);
        shadowUV.y *= 2.8;
        float shadowDist = length(shadowUV);
        if (shadowDist < rMax * 2.2) {
          float groundFalloff = smoothstep(rMax * 2.2, 0.0, shadowDist);
          vec3 groundGlow = mix(
            vec3(1.0, 0.42, 0.36) * 0.28, // Coral
            vec3(0.25, 0.84, 1.0) * 0.25, // Cyan
            sin(u_time * 0.4 + st.x * 2.0) * 0.5 + 0.5
          );
          finalColor += groundGlow * groundFalloff * (0.45 + u_amp * 0.4);
        }

        // Ambient radial backglow directly behind the orb
        float backglow = smoothstep(rMax * 2.6, 0.0, dist);
        finalColor += vec3(1.0, 0.42, 0.36) * backglow * 0.12 * (0.6 + u_amp * 0.5);
        finalColor += vec3(0.25, 0.84, 1.0) * smoothstep(rMax * 1.8, 0.0, dist) * 0.08;

        // If inside the sphere volume (with organic deformation)
        float angle = atan(p.y, p.x);
        float t = u_time * 0.6;
        
        // Fluid displacement based on noise and audio/voice amplitude
        vec3 noiseCoord = vec3(p * 2.8, t * 0.28);
        float n1 = snoise(noiseCoord);
        float n2 = snoise(noiseCoord * 2.1 + vec3(1.7, 4.3, 0.5));
        float disp = (n1 * 0.06 + n2 * 0.03) * (0.4 + u_amp * 1.6);
        float currentRadius = rMax * (1.0 + disp);

        if (dist < currentRadius) {
          // Reconstruct 3D surface normal (hemisphere raycast)
          float z = sqrt(max(0.0, currentRadius * currentRadius - dist * dist));
          vec3 normal = normalize(vec3(p.x, p.y, z));

          // Perturb normal with noise gradients for fluid liquid distortion
          vec3 pNorm = normal;
          pNorm.x += snoise(normal * 3.2 + vec3(t * 0.4, 0.0, 0.0)) * 0.18 * (0.5 + u_amp);
          pNorm.y += snoise(normal * 3.2 + vec3(0.0, t * 0.4, 0.0)) * 0.18 * (0.5 + u_amp);
          pNorm = normalize(pNorm);

          // View vector & Fresnel
          vec3 view = vec3(0.0, 0.0, 1.0);
          float fresnel = pow(1.0 - max(dot(pNorm, view), 0.0), 2.8);

          // Optical Iridescence / Thin-film dispersion (matching reference)
          // Gradient components: Coral, Magenta, Electric Cyan, Gold, Violet
          float iriPhase = fresnel * 1.4 + normal.y * 0.6 + sin(normal.x * 2.5 + t * 0.3) * 0.4;
          vec3 iriCol = iridescence(
            iriPhase,
            vec3(0.5, 0.45, 0.55),     // Brightness base
            vec3(0.55, 0.5, 0.45),     // Amplitude
            vec3(1.0, 1.0, 1.0),       // Frequency
            vec3(0.0, 0.33, 0.67)      // Phase shift (RGB Rainbow dispersion)
          );

          // Accent color shifts:
          // Key highlights: Deep electric cyan + hot molten coral/amber
          vec3 coralFlame = vec3(1.0, 0.42, 0.36);
          vec3 electricCyan = vec3(0.25, 0.84, 1.0);
          vec3 moltenGold = vec3(1.0, 0.72, 0.22);
          vec3 deepViolet = vec3(0.7, 0.35, 1.0);

          vec3 liquidSurface = mix(coralFlame, electricCyan, smoothstep(-0.3, 0.6, normal.x + sin(t*0.5)*0.3));
          liquidSurface = mix(liquidSurface, moltenGold, smoothstep(0.2, 0.8, -normal.y));
          liquidSurface = mix(liquidSurface, deepViolet, smoothstep(0.5, 1.0, fresnel));

          // Deep fluid core (dark rich obsidian interior)
          float coreDark = smoothstep(currentRadius * 0.95, 0.0, dist);
          vec3 coreCol = vec3(0.04, 0.06, 0.1) * (1.0 - coreDark * 0.7);

          // Specular highlights (Glossy studio reflections)
          vec3 lightDir1 = normalize(vec3(0.5, 0.7, 1.0));
          vec3 lightDir2 = normalize(vec3(-0.7, -0.4, 0.8));
          vec3 half1 = normalize(lightDir1 + view);
          vec3 half2 = normalize(lightDir2 + view);
          float spec1 = pow(max(dot(pNorm, half1), 0.0), 32.0);
          float spec2 = pow(max(dot(pNorm, half2), 0.0), 48.0);
          vec3 spec = vec3(1.0) * spec1 * 0.9 + electricCyan * spec2 * 0.6;

          // Rim glow / Edge chromatic dispersion
          float edge = smoothstep(currentRadius, currentRadius - 0.012, dist);
          vec3 orbColor = mix(coreCol, liquidSurface, fresnel * 0.85 + 0.15);
          orbColor += iriCol * fresnel * 0.75;
          orbColor += spec;

          // React to speech/listen states
          if (u_state > 1.5) {
            // Speaking: intensified core warmth
            orbColor += coralFlame * u_amp * 0.5;
          } else if (u_state > 0.5) {
            // Listening: energetic cyan scanlines
            float scan = sin(p.y * 80.0 - u_time * 6.0) * 0.5 + 0.5;
            orbColor += electricCyan * scan * 0.25 * u_amp;
          }

          finalColor = mix(finalColor, orbColor, edge);
        }

        gl_FragColor = vec4(finalColor, 1.0);
      }
    `;

    function createShader(glCtx: WebGLRenderingContext, type: number, source: string) {
      const shader = glCtx.createShader(type);
      if (!shader) return null;
      glCtx.shaderSource(shader, source);
      glCtx.compileShader(shader);
      if (!glCtx.getShaderParameter(shader, glCtx.COMPILE_STATUS)) {
        console.warn('Shader compile failed:', glCtx.getShaderInfoLog(shader));
        glCtx.deleteShader(shader);
        return null;
      }
      return shader;
    }

    if (gl) {
      const vs = createShader(gl, gl.VERTEX_SHADER, vsSource);
      const fs = createShader(gl, gl.FRAGMENT_SHADER, fsSource);
      if (vs && fs) {
        program = gl.createProgram();
        if (program) {
          gl.attachShader(program, vs);
          gl.attachShader(program, fs);
          gl.linkProgram(program);
          if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
            console.warn('Program link failed:', gl.getProgramInfoLog(program));
            program = null;
          }
        }
      }
    }

    // Quad geometry for WebGL
    let posBuffer: WebGLBuffer | null = null;
    let posAttr = -1;
    let uResLoc: WebGLUniformLocation | null = null;
    let uTimeLoc: WebGLUniformLocation | null = null;
    let uMouseLoc: WebGLUniformLocation | null = null;
    let uAmpLoc: WebGLUniformLocation | null = null;
    let uStateLoc: WebGLUniformLocation | null = null;
    let uCenterLoc: WebGLUniformLocation | null = null;
    let uRadiusLoc: WebGLUniformLocation | null = null;

    if (gl && program) {
      posBuffer = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, posBuffer);
      gl.bufferData(
        gl.ARRAY_BUFFER,
        new Float32Array([-1, -1, 1, -1, -1, 1, -1, 1, 1, -1, 1, 1]),
        gl.STATIC_DRAW
      );
      posAttr = gl.getAttribLocation(program, 'a_pos');
      uResLoc = gl.getUniformLocation(program, 'u_res');
      uTimeLoc = gl.getUniformLocation(program, 'u_time');
      uMouseLoc = gl.getUniformLocation(program, 'u_mouse');
      uAmpLoc = gl.getUniformLocation(program, 'u_amp');
      uStateLoc = gl.getUniformLocation(program, 'u_state');
      uCenterLoc = gl.getUniformLocation(program, 'u_center');
      uRadiusLoc = gl.getUniformLocation(program, 'u_radius');
    }

    // 2D Canvas Fallback in case WebGL is disabled or unsupported
    const ctx2d = !gl ? cv.getContext('2d') : null;

    function render2d(w: number, h: number, t: number, cx: number, cy: number, r: number) {
      if (!ctx2d) return;
      ctx2d.clearRect(0, 0, w, h);
      ctx2d.fillStyle = '#06080d';
      ctx2d.fillRect(0, 0, w, h);

      // Floor glow
      const fg = ctx2d.createRadialGradient(cx, cy + r * 1.1, 0, cx, cy + r * 1.1, r * 1.8);
      fg.addColorStop(0, 'rgba(255, 107, 91, 0.25)');
      fg.addColorStop(0.5, 'rgba(63, 214, 255, 0.12)');
      fg.addColorStop(1, 'transparent');
      ctx2d.fillStyle = fg;
      ctx2d.beginPath();
      ctx2d.ellipse(cx, cy + r * 1.1, r * 1.6, r * 0.45, 0, 0, Math.PI * 2);
      ctx2d.fill();

      // Iridescent Orb
      const og = ctx2d.createRadialGradient(cx - r * 0.3, cy - r * 0.35, r * 0.05, cx, cy, r);
      og.addColorStop(0, '#ffffff');
      og.addColorStop(0.2, '#ff6b5b');
      og.addColorStop(0.55, '#3fd6ff');
      og.addColorStop(0.85, '#9945ff');
      og.addColorStop(1, '#0e1420');

      ctx2d.save();
      ctx2d.shadowColor = 'rgba(255, 107, 91, 0.45)';
      ctx2d.shadowBlur = 40;
      ctx2d.fillStyle = og;
      ctx2d.beginPath();
      ctx2d.arc(cx, cy, r, 0, Math.PI * 2);
      ctx2d.fill();
      ctx2d.restore();
    }

    function render() {
      const now = performance.now();
      const elapsed = (now - startTime) * 0.001;

      // Mouse inertia damping
      mouse.x += (mouse.targetX - mouse.x) * 0.06;
      mouse.y += (mouse.targetY - mouse.y) * 0.06;

      // Assistant state analysis
      const st = stateRef.current || { screen: 'home', listening: false, speaking: false };
      targetAmp = st.speaking ? 0.95 : st.listening ? 0.65 : st.screen === 'voice' ? 0.45 : 0.22;
      currentAmp += (targetAmp - currentAmp) * 0.08;

      let stateMode = 0.0; // idle
      if (st.speaking) stateMode = 2.0;
      else if (st.listening) stateMode = 1.0;

      const dpr = Math.min(2, window.devicePixelRatio || 1);
      const w = cv!.clientWidth;
      const h = cv!.clientHeight;

      if (cv!.width !== Math.round(w * dpr) || cv!.height !== Math.round(h * dpr)) {
        cv!.width = Math.round(w * dpr);
        cv!.height = Math.round(h * dpr);
      }

      // Orb Center & Radius depending on screen
      let cx = w * 0.5 * dpr;
      let cy = h * 0.42 * dpr;
      let r = Math.min(w, h) * 0.17 * dpr;

      if (st.screen === 'home') {
        // Asymmetric editorial balance from reference: slightly offset right of center on wide screens
        const isWide = w > 1100;
        cx = (isWide ? w * 0.52 : w * 0.5) * dpr;
        cy = (isWide ? h * 0.36 : h * 0.34) * dpr;
        r = Math.min(w, h) * (isWide ? 0.15 : 0.13) * dpr;
      } else if (st.screen === 'voice') {
        cx = w * 0.5 * dpr;
        cy = h * 0.42 * dpr;
        r = Math.min(w, h) * 0.22 * dpr;
      } else {
        // Tab screens: shrink to compact status beacon in top-right
        cx = (w - 70) * dpr;
        cy = 55 * dpr;
        r = 24 * dpr;
      }

      if (gl && program && posBuffer) {
        gl.viewport(0, 0, cv!.width, cv!.height);
        gl.useProgram(program);

        gl.enableVertexAttribArray(posAttr);
        gl.bindBuffer(gl.ARRAY_BUFFER, posBuffer);
        gl.vertexAttribPointer(posAttr, 2, gl.FLOAT, false, 0, 0);

        gl.uniform2f(uResLoc, cv!.width, cv!.height);
        gl.uniform1f(uTimeLoc, elapsed);
        gl.uniform2f(uMouseLoc, mouse.x, mouse.y);
        gl.uniform1f(uAmpLoc, currentAmp);
        gl.uniform1f(uStateLoc, stateMode);
        gl.uniform2f(uCenterLoc, cx, cy);
        gl.uniform1f(uRadiusLoc, r);

        gl.drawArrays(gl.TRIANGLES, 0, 6);
      } else {
        render2d(w, h, elapsed, cx / dpr, cy / dpr, r / dpr);
      }

      animId = requestAnimationFrame(render);
    }

    render();

    return () => {
      cancelAnimationFrame(animId);
      window.removeEventListener('mousemove', onMouseMove);
    };
  }, [canvasRef, stateRef]);
}
