using Xunit;

// TestKeys holds process-wide static state (the RSA signing key, the JWKS fetch counter), and
// JwtTests.An_unknown_kid_refetches_the_jwks_once calls TestKeys.Rotate(). xUnit runs different
// test classes (different collections) in parallel by default, so a rotation racing another
// class's mint or validation is a real hazard, not a hypothetical one. Serializing collections
// costs wall-clock time; for a suite this size that is the right trade against a flaky race.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
