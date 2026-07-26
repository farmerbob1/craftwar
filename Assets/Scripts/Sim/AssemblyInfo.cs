using System.Runtime.CompilerServices;

// The test assembly needs the internals the serializer exposes — the free-slot
// list, the raw tile array, the terrain planes. They are internal rather than
// public on purpose: they are authoritative state that only SimSerializer should
// be reaching for, and the round-trip tests have to be able to compare them
// directly because none of them is covered by the state hash.
//
// The standalone dotnet harness compiles Sim and the tests into ONE assembly, so
// it never needed this — which is exactly why it went unnoticed there and only
// surfaced in the editor.
[assembly: InternalsVisibleTo("Craftwar.Sim.Tests")]
