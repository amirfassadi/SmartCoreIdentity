import {createBridge} from '@/lib/bff/bridge.mjs';

export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';
export const maxDuration = 60;
const handle = (request: Request) => createBridge(process.env)(request);
export {handle as GET, handle as POST, handle as PUT, handle as PATCH,
  handle as DELETE, handle as OPTIONS, handle as HEAD};
