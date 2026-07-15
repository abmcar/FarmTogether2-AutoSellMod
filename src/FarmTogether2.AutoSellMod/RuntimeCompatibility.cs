using System;
using System.IO;
using System.Security.Cryptography;

namespace FarmTogether2.AutoSellMod
{
    internal readonly struct RuntimeCompatibilityResult
    {
        internal RuntimeCompatibilityResult(bool isCompatible, string message)
        {
            IsCompatible = isCompatible;
            Message = message;
        }

        internal bool IsCompatible { get; }
        internal string Message { get; }
    }

    internal static class RuntimeCompatibility
    {
        internal const string SupportedSteamBuild = "24184988";
        internal const string ExpectedGameAssemblySha256 =
            "f5a611e417f46c60371d27f999e77bcedee4fc00d6062836c9d6682d52535f29";
        internal const string ExpectedGlobalMetadataSha256 =
            "ca87d94ee64b8a8a0ceca8f9c99b9e2226afc97bbc760a14461b673067df3320";

        internal static RuntimeCompatibilityResult VerifyCurrentGame(
            string gameRootPath,
            string gameDataPath)
        {
            return VerifyFiles(
                Path.Combine(gameRootPath, "GameAssembly.dll"),
                Path.Combine(gameDataPath, "il2cpp_data", "Metadata", "global-metadata.dat"),
                ExpectedGameAssemblySha256,
                ExpectedGlobalMetadataSha256);
        }

        internal static RuntimeCompatibilityResult VerifyFiles(
            string gameAssemblyPath,
            string metadataPath,
            string expectedGameAssemblySha256,
            string expectedMetadataSha256)
        {
            RuntimeCompatibilityResult gameAssemblyResult = VerifyFile(
                "GameAssembly.dll",
                gameAssemblyPath,
                expectedGameAssemblySha256);
            if (!gameAssemblyResult.IsCompatible)
                return gameAssemblyResult;

            RuntimeCompatibilityResult metadataResult = VerifyFile(
                "global-metadata.dat",
                metadataPath,
                expectedMetadataSha256);
            if (!metadataResult.IsCompatible)
                return metadataResult;

            return new RuntimeCompatibilityResult(
                true,
                $"binary fingerprints match supported Steam build {SupportedSteamBuild}");
        }

        private static RuntimeCompatibilityResult VerifyFile(
            string displayName,
            string path,
            string expectedSha256)
        {
            if (!File.Exists(path))
            {
                return new RuntimeCompatibilityResult(
                    false,
                    $"{displayName} was not found at '{path}'");
            }

            try
            {
                string actualSha256 = ComputeSha256(path);
                if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new RuntimeCompatibilityResult(
                        false,
                        $"{displayName} fingerprint {actualSha256} does not match supported Steam build {SupportedSteamBuild}");
                }

                return new RuntimeCompatibilityResult(true, $"{displayName} fingerprint matches");
            }
            catch (IOException exception)
            {
                return ReadFailure(displayName, exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return ReadFailure(displayName, exception);
            }
            catch (CryptographicException exception)
            {
                return ReadFailure(displayName, exception);
            }
        }

        private static RuntimeCompatibilityResult ReadFailure(
            string displayName,
            Exception exception)
        {
            return new RuntimeCompatibilityResult(
                false,
                $"could not fingerprint {displayName}: {exception.GetType().Name}: {exception.Message}");
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        }
    }
}
