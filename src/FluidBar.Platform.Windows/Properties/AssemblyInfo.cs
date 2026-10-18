using System.Runtime.CompilerServices;

// The platform test project exercises internal pure helpers (DDC/WMI parsing, COM payload marshalling
// shapes, notification parsing) without exposing those interop details as public API.
[assembly: InternalsVisibleTo("FluidBar.Platform.Tests")]
