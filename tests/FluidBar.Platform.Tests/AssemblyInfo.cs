using Xunit;

// LayeredWindowHost is thread-affine and creates real HWNDs; running its tests in
// parallel with other collections makes them flaky. Keep the Platform suite serial.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
