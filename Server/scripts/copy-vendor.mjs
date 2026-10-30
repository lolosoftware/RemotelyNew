import { cp, mkdir, access } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = fileURLToPath(new URL('../', import.meta.url));
async function copy(source, target) {
    const dest=path.join(root,'wwwroot/lib',target);
    await mkdir(path.dirname(dest),{recursive:true});
    await cp(path.join(root,'node_modules',source),dest,{recursive:true});
}
await copy('@microsoft/signalr/dist/browser','microsoft-signalr');
await copy('@microsoft/signalr-protocol-msgpack/dist/browser','microsoft/signalr-protocol-msgpack/dist/browser');
await copy('@msgpack/msgpack/dist.umd/msgpack.js','msgpack/msgpack.js');
await copy('@msgpack/msgpack/LICENSE','msgpack/LICENSE');
await copy('@fortawesome/fontawesome-free/css','fontawesome/css');
await copy('@fortawesome/fontawesome-free/webfonts','fontawesome/webfonts');
await copy('@fortawesome/fontawesome-free/LICENSE.txt','fontawesome/LICENSE.txt');
