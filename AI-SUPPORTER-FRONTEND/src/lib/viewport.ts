import { useEffect, useState } from 'react';

export interface Viewport {
  w: number;
  h: number;
  compact: boolean;
  wide: boolean;
  ultra: boolean;
}

function measure(): Viewport {
  const w = window.innerWidth;
  const h = window.innerHeight;
  return { w, h, compact: w < 1280, wide: w >= 1680, ultra: w >= 2560 };
}

export function useViewport(): Viewport {
  const [vp, setVp] = useState<Viewport>(measure);
  useEffect(() => {
    const onResize = () => setVp(measure());
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, []);
  return vp;
}