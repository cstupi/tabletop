import type { Orientation } from '$lib/api/types';

const rad = (d: number) => (d * Math.PI) / 180;

export const eulerFor = (o: Orientation) => ({ x: rad(o.pitch), y: rad(o.roll), z: rad(o.yaw), order: 'ZYX' as const });
