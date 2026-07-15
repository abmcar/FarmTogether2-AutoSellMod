using System;
using System.IO;
using Xunit;

namespace FarmTogether2.AutoSellMod;

public sealed class RuntimeCompatibilityTests
{
    [Fact]
    public void CompatibilityConstantsMatchTheVerifiedSteamBuild()
    {
        Assert.Equal("24069957", RuntimeCompatibility.SupportedSteamBuild);
        Assert.Equal(
            "72b6e96a73b931dafaf96f2fbbb29ac5f1c4723916d5baf6d2e01ace1c703309",
            RuntimeCompatibility.ExpectedGameAssemblySha256);
        Assert.Equal(
            "b80a90c0681d404cec6f015eae9914c671297912ebf0aad7d8e27c68b7801d2d",
            RuntimeCompatibility.ExpectedGlobalMetadataSha256);
    }

    [Fact]
    public void ExactBinaryFingerprintsAreAccepted()
    {
        using var files = RuntimeFiles.Create("game-binary", "metadata");

        RuntimeCompatibilityResult result = RuntimeCompatibility.VerifyFiles(
            files.GameAssemblyPath,
            files.MetadataPath,
            "509a64c1606a8ac3fccc1e6ebba7f462d7a8a9ad80899e9069fe5b7bdf55b0f9",
            "45447b7afbd5e544f7d0f1df0fccd26014d9850130abd3f020b89ff96b82079f");

        Assert.True(result.IsCompatible, result.Message);
    }

    [Fact]
    public void ChangedGameBinaryFailsClosed()
    {
        using var files = RuntimeFiles.Create("changed", "metadata");

        RuntimeCompatibilityResult result = RuntimeCompatibility.VerifyFiles(
            files.GameAssemblyPath,
            files.MetadataPath,
            "509a64c1606a8ac3fccc1e6ebba7f462d7a8a9ad80899e9069fe5b7bdf55b0f9",
            "45447b7afbd5e544f7d0f1df0fccd26014d9850130abd3f020b89ff96b82079f");

        Assert.False(result.IsCompatible);
        Assert.Contains("GameAssembly", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFingerprintInputFailsClosed()
    {
        using var files = RuntimeFiles.Create("game-binary", "metadata");
        File.Delete(files.MetadataPath);

        RuntimeCompatibilityResult result = RuntimeCompatibility.VerifyFiles(
            files.GameAssemblyPath,
            files.MetadataPath,
            "509a64c1606a8ac3fccc1e6ebba7f462d7a8a9ad80899e9069fe5b7bdf55b0f9",
            "45447b7afbd5e544f7d0f1df0fccd26014d9850130abd3f020b89ff96b82079f");

        Assert.False(result.IsCompatible);
        Assert.Contains("global-metadata", result.Message, StringComparison.Ordinal);
    }

    private sealed class RuntimeFiles : IDisposable
    {
        private RuntimeFiles(string root)
        {
            Root = root;
            GameAssemblyPath = Path.Combine(root, "GameAssembly.dll");
            MetadataPath = Path.Combine(root, "global-metadata.dat");
        }

        internal string Root { get; }
        internal string GameAssemblyPath { get; }
        internal string MetadataPath { get; }

        internal static RuntimeFiles Create(string gameAssembly, string metadata)
        {
            var files = new RuntimeFiles(Path.Combine(
                Path.GetTempPath(),
                "FarmTogether2.AutoSellMod.Tests",
                Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(files.Root);
            File.WriteAllText(files.GameAssemblyPath, gameAssembly);
            File.WriteAllText(files.MetadataPath, metadata);
            return files;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
