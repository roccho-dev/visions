using System.Security.Cryptography;

namespace Visions.Core;

// Explicitly selected, already exported files only. No access to an application's database.
public static class FileExtractor
{
    public const long MaxBytes = 32 * 1024 * 1024;
    public static string Extract(string inputPath, string workspace)
    {
        if (!System.IO.Path.IsPathFullyQualified(inputPath) || inputPath.StartsWith("\\\\", StringComparison.Ordinal) || inputPath.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("local_file_required");
        var reference = Contract.Id();
        var artifacts = System.IO.Path.Combine(workspace, "artifacts");
        Directory.CreateDirectory(artifacts);
        var destination = System.IO.Path.Combine(artifacts, reference + ".bin");
        using var capture = new CaptureWriter(System.IO.Path.Combine(workspace, "captures"), "selected-file");
        var recorded = false;
        try
        {
            using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > MaxBytes) throw new IOException("artifact_limit");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[65536];
                int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    total += read;
                    if (total > MaxBytes) throw new IOException("artifact_limit");
                    hash.AppendData(buffer, 0, read);
                    output.Write(buffer, 0, read);
                }
                output.Flush(true);
            }
            capture.Artifact(reference, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), total);
            recorded = true;
            capture.End("completed");
            return capture.Path;
        }
        catch
        {
            // Once an artifact record is durable, preserve the bytes it references even if the end write fails.
            if (!recorded && File.Exists(destination)) File.Delete(destination);
            throw;
        }
    }
}
