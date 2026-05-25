// Stub for wasi:sockets/udp@0.2.0.
// .NET NativeAOT imports this interface at the WASM ABI level but the app never calls UDP sockets.
// The real preview2-shim/sockets depends on Node.js worker-thread APIs unavailable in Cloudflare Workers.
export class UdpSocket {}
export class IncomingDatagramStream {}
export class OutgoingDatagramStream {}
