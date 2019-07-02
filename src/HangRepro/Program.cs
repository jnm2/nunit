using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HangRepro
{
    public static class Program
    {
        public static async Task Main()
        {
            var nunitConsolePath = await GetNUnitConsolePathAsync();

            var results = new BlockingCollection<bool>();

            const int total = 100;
            var remaining = total;

            for (var i = 0; i < 4; i++)
            {
                new Thread(() =>
                {
                    while (Interlocked.Decrement(ref remaining) >= 0)
                    {
                        VerifyRepro(nunitConsolePath, notifyResult: results.Add);
                    }
                }).Start();
            }

            var reproCount = 0;

            for (var progress = 1; progress <= total; progress++)
            {
                var nextResult = results.Take();
                if (nextResult) reproCount++;

                Console.Write($"\r{reproCount} repros/{progress} tries, {reproCount / (double)progress:p1}");
            }

            Console.WriteLine();
        }

        public static void VerifyRepro(string nunitConsolePath, Action<bool> notifyResult)
        {
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    FileName = nunitConsolePath,
                    Arguments = $"\"{typeof(Program).Assembly.Location}\" --inprocess --noresult --trace=info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            })
            {
                var didRepro = false;

                process.OutputDataReceived += (sender, e) => { };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (didRepro) return;

                    if (e.Data != null)
                    {
                        didRepro = true;
                        notifyResult.Invoke(true);

                        try
                        {
                            process.Kill();
                        }
                        catch (InvalidOperationException) // Possible exception if process exits before we kill
                        {
                        }
                        catch (Win32Exception) // Possible exception if process exits before we kill
                        {
                        }
                    }
                    else
                    {
                        notifyResult.Invoke(false);
                    }
                };

                process.Start();
                var pid = process.Id;

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                File.Delete($"InternalTrace.{pid}.log");
                if (!didRepro) File.Delete($"InternalTrace.{pid}.HangRepro.exe.log");
            }
        }

        private static async Task<string> GetNUnitConsolePathAsync()
        {
            var assemblyDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);

            var targetDirectory = Path.Combine(assemblyDirectory, @"tools\NUnit.ConsoleRunner");
            var path = Path.Combine(targetDirectory, @"nunit3-console.exe");
            const string packageDownloadUrl = "https://github.com/nunit/nunit-console/releases/download/v3.10/NUnit.ConsoleRunner.3.10.0.nupkg";

            if (!File.Exists(path))
            {
                Console.WriteLine($"Downloading {packageDownloadUrl} to {targetDirectory}...");

                using (var client = new HttpClient())
                using (var stream = await client.GetStreamAsync(packageDownloadUrl))
                using (var archive = new ZipArchive(stream))
                {
                    archive.ExtractSubdirectoryToDirectory("tools", targetDirectory);
                }

                Console.WriteLine("Download complete.");
                Console.WriteLine();
            }

            return path;
        }

        private static void ExtractSubdirectoryToDirectory(this ZipArchive zipArchive, string zipSubdirectory, string destinationDirectoryName)
        {
            if (zipArchive is null) throw new ArgumentNullException(nameof(zipArchive));

            zipSubdirectory = zipSubdirectory.Replace('\\', '/');
            if (zipSubdirectory.Last() != '/') zipSubdirectory += '/';

            if (destinationDirectoryName.Last() != Path.DirectorySeparatorChar)
                destinationDirectoryName += Path.DirectorySeparatorChar;

            foreach (var entry in zipArchive.Entries)
            {
                if (!entry.FullName.StartsWith(zipSubdirectory)) continue;

                var destinationPath = Path.Combine(destinationDirectoryName, entry.FullName.Substring(zipSubdirectory.Length));

                if (!destinationPath.StartsWith(destinationDirectoryName))
                    throw new InvalidOperationException("The zip archive path would write outside the specified root folder.");

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                entry.ExtractToFile(destinationPath);
            }
        }
    }
}
