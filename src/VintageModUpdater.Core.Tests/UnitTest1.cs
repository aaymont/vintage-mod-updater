using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using VintageModUpdater.Core;

namespace VintageModUpdater.Core.Tests;

public sealed class SecurityHardeningTests
{
    [Fact]
    public async Task InstallUpdateAsync_AllowsOfficialModDbCdnRedirect()
    {
        using var temp = new TempDirectory();
        var modsPath = Path.Combine(temp.Path, "Mods");
        Directory.CreateDirectory(modsPath);

        var installedPath = Path.Combine(modsPath, "awearablelight-v1.1.9.zip");
        await File.WriteAllTextAsync(installedPath, "installed");

        var installedMod = new InstalledMod(
            Identifier: "awearablelight",
            Name: "A Wearable Light",
            Version: "1.1.9",
            Path: installedPath,
            FileName: "awearablelight-v1.1.9.zip",
            IsDirectory: false,
            Authors: Array.Empty<string>(),
            GameVersions: Array.Empty<string>(),
            Error: null);

        var update = new ModUpdateStatus(
            ModId: "awearablelight",
            CurrentVersion: "1.1.9",
            Kind: ModUpdateKind.UpdateAvailable,
            AvailableVersion: "1.2.0",
            DownloadFileName: "awearablelight-v1.2.0.zip",
            DownloadUrl: "https://mods.vintagestory.at/download/96890/awearablelight-v1.2.0.zip",
            ErrorCode: null,
            Message: "update available");

        var payload = CreateZipWithMod("awearablelight", "1.2.0");
        var handler = new CdnRedirectResponseHandler(payload);
        using var httpClient = new HttpClient(handler);
        var installer = new ModUpdateInstaller(new BackupService(), httpClient);

        var result = await installer.InstallUpdateAsync(installedMod, update, modsPath);

        Assert.Equal(Path.Combine(modsPath, "awearablelight-v1.2.0.zip"), result.DestinationPath);
        Assert.True(File.Exists(result.DestinationPath));
    }

    [Fact]
    public async Task InstallUpdateAsync_ReplacesZipModAndCreatesBackup()
    {
        using var temp = new TempDirectory();
        var modsPath = Path.Combine(temp.Path, "Mods");
        Directory.CreateDirectory(modsPath);

        var installedPath = Path.Combine(modsPath, "examplemod.zip");
        await File.WriteAllTextAsync(installedPath, "installed");

        var installedMod = new InstalledMod(
            Identifier: "examplemod",
            Name: "Example Mod",
            Version: "1.0.0",
            Path: installedPath,
            FileName: "examplemod.zip",
            IsDirectory: false,
            Authors: Array.Empty<string>(),
            GameVersions: Array.Empty<string>(),
            Error: null);

        var update = new ModUpdateStatus(
            ModId: "examplemod",
            CurrentVersion: "1.0.0",
            Kind: ModUpdateKind.UpdateAvailable,
            AvailableVersion: "1.1.0",
            DownloadFileName: "examplemod-1.1.0.zip",
            DownloadUrl: "https://mods.vintagestory.at/files/examplemod-1.1.0.zip",
            ErrorCode: null,
            Message: "update available");

        var payload = CreateZipWithMod("examplemod", "1.1.0");
        var handler = new StaticResponseHandler(payload);
        using var httpClient = new HttpClient(handler);
        var installer = new ModUpdateInstaller(new BackupService(), httpClient);

        var result = await installer.InstallUpdateAsync(installedMod, update, modsPath);

        var destinationPath = Path.Combine(modsPath, "examplemod-1.1.0.zip");
        Assert.True(File.Exists(destinationPath));
        Assert.False(File.Exists(installedPath));
        Assert.Equal("1.1.0", result.InstalledVersion);
        Assert.Equal(destinationPath, result.DestinationPath);
        Assert.True(File.Exists(result.LogPath));
        Assert.True(Directory.Exists(BackupService.GetBackupRoot(modsPath)));
    }

    [Fact]
    public async Task InstallUpdateAsync_RejectsArchiveWithUnexpectedVersion()
    {
        using var temp = new TempDirectory();
        var modsPath = Path.Combine(temp.Path, "Mods");
        Directory.CreateDirectory(modsPath);

        var installedPath = Path.Combine(modsPath, "examplemod.zip");
        await File.WriteAllTextAsync(installedPath, "installed");

        var installedMod = new InstalledMod(
            Identifier: "examplemod",
            Name: "Example Mod",
            Version: "1.0.0",
            Path: installedPath,
            FileName: "examplemod.zip",
            IsDirectory: false,
            Authors: Array.Empty<string>(),
            GameVersions: Array.Empty<string>(),
            Error: null);

        var update = new ModUpdateStatus(
            ModId: "examplemod",
            CurrentVersion: "1.0.0",
            Kind: ModUpdateKind.UpdateAvailable,
            AvailableVersion: "1.1.0",
            DownloadFileName: "examplemod-1.1.0.zip",
            DownloadUrl: "https://mods.vintagestory.at/files/examplemod-1.1.0.zip",
            ErrorCode: null,
            Message: "update available");

        var payload = CreateZipWithMod("examplemod", "1.0.0");
        var handler = new StaticResponseHandler(payload);
        using var httpClient = new HttpClient(handler);
        var installer = new ModUpdateInstaller(new BackupService(), httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.InstallUpdateAsync(installedMod, update, modsPath));
        Assert.Contains("does not match the expected update version", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(installedPath));
    }

    [Fact]
    public async Task InstallUpdateAsync_RejectsArchiveWithMismatchedModId()
    {
        using var temp = new TempDirectory();
        var modsPath = Path.Combine(temp.Path, "Mods");
        Directory.CreateDirectory(modsPath);

        var installedPath = Path.Combine(modsPath, "examplemod.zip");
        await File.WriteAllTextAsync(installedPath, "installed");

        var installedMod = new InstalledMod(
            Identifier: "examplemod",
            Name: "Example Mod",
            Version: "1.0.0",
            Path: installedPath,
            FileName: "examplemod.zip",
            IsDirectory: false,
            Authors: Array.Empty<string>(),
            GameVersions: Array.Empty<string>(),
            Error: null);

        var update = new ModUpdateStatus(
            ModId: "examplemod",
            CurrentVersion: "1.0.0",
            Kind: ModUpdateKind.UpdateAvailable,
            AvailableVersion: "1.1.0",
            DownloadFileName: "examplemod-1.1.0.zip",
            DownloadUrl: "https://mods.vintagestory.at/files/examplemod-1.1.0.zip",
            ErrorCode: null,
            Message: "update available");

        var payload = CreateZipWithModId("differentmod");
        var handler = new StaticResponseHandler(payload);
        using var httpClient = new HttpClient(handler);
        var installer = new ModUpdateInstaller(new BackupService(), httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.InstallUpdateAsync(installedMod, update, modsPath));
        Assert.Contains("downloaded archive targets", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateBackupAsync_AllowsValidUnixModsPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var temp = new TempDirectory();
        var modsPath = Path.Combine(temp.Path, "Mods");
        Directory.CreateDirectory(modsPath);

        var installedPath = Path.Combine(modsPath, "examplemod.zip");
        await File.WriteAllTextAsync(installedPath, "installed");

        var installedMod = new InstalledMod(
            Identifier: "examplemod",
            Name: "Example Mod",
            Version: "1.0.0",
            Path: installedPath,
            FileName: "examplemod.zip",
            IsDirectory: false,
            Authors: Array.Empty<string>(),
            GameVersions: Array.Empty<string>(),
            Error: null);

        var service = new BackupService();
        var backup = await service.CreateBackupAsync(installedMod, modsPath);

        Assert.Equal("examplemod", backup.ModId);
    }

    [Fact]
    public async Task RestoreAsync_RejectsWhenManifestMetadataChanged()
    {
        using var temp = new TempDirectory();
        var modsPath = Path.Combine(temp.Path, "Mods");
        var backupDir = Path.Combine(modsPath, ".vintage-mod-updater", "backups", "20260531130000_examplemod_1.0.0");
        Directory.CreateDirectory(backupDir);

        var backupFile = Path.Combine(backupDir, "examplemod.zip");
        await File.WriteAllTextAsync(backupFile, "payload");
        var originalPath = Path.Combine(modsPath, "examplemod.zip");

        var manifestJson = """
        {
          "Id": "20260531130000_examplemod_1.0.0",
          "ModId": "examplemod",
          "ModName": "Renamed Mod",
          "Version": "1.0.0",
          "OriginalPath": "__ORIGINAL_PATH__",
          "BackupPath": "__BACKUP_PATH__",
          "IsDirectory": false,
          "CreatedAt": "2026-05-31T13:00:00+00:00"
        }
        """
            .Replace("__ORIGINAL_PATH__", originalPath.Replace("\\", "\\\\"))
            .Replace("__BACKUP_PATH__", backupFile.Replace("\\", "\\\\"));
        await File.WriteAllTextAsync(Path.Combine(backupDir, "backup.json"), manifestJson, Encoding.UTF8);

        var staleEntry = new BackupEntry(
            Id: "20260531130000_examplemod_1.0.0",
            ModId: "examplemod",
            ModName: "Example Mod",
            Version: "1.0.0",
            OriginalPath: originalPath,
            BackupPath: backupFile,
            IsDirectory: false,
            CreatedAt: DateTimeOffset.Parse("2026-05-31T13:00:00+00:00"));

        var service = new BackupService();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(staleEntry));
        Assert.Contains("metadata changed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scan_RejectsOversizedModInfoInZip()
    {
        using var temp = new TempDirectory();
        var modsPath = Path.Combine(temp.Path, "Mods");
        Directory.CreateDirectory(modsPath);

        var zipPath = Path.Combine(modsPath, "oversized.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("modinfo.json", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
            writer.Write("{\"modid\":\"example\",\"name\":\"");
            writer.Write(new string('a', 300_000));
            writer.Write("\"}");
        }

        var scanner = new ModScanner();
        var mods = scanner.Scan(modsPath);

        Assert.Single(mods);
        Assert.NotNull(mods[0].Error);
    }

    [Fact]
    public async Task CheckUpdatesAsync_RejectsUnexpectedFinalApiHost()
    {
        var body = """
        {
          "data": {
            "examplemod": {
              "fileName": "examplemod.zip",
              "fileUrl": "https://mods.vintagestory.at/files/examplemod.zip",
              "recommendedUpgrade": "1.1.0"
            }
          }
        }
        """;

        var handler = new DynamicResponseHandler((request) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/api/v2/mods/install-information"),
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return response;
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://mods.vintagestory.at")
        };
        var client = new ModDbClient(httpClient);
        var mod = new InstalledMod(
            Identifier: "examplemod",
            Name: "Example Mod",
            Version: "1.0.0",
            Path: "examplemod.zip",
            FileName: "examplemod.zip",
            IsDirectory: false,
            Authors: Array.Empty<string>(),
            GameVersions: Array.Empty<string>(),
            Error: null);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CheckUpdatesAsync(new[] { mod }, "1.20.0"));
    }

    [Fact]
    public async Task InstallUpdateAsync_RejectsNonHttpsDownloadUrl()
    {
        using var temp = new TempDirectory();
        var modsPath = Path.Combine(temp.Path, "Mods");
        Directory.CreateDirectory(modsPath);
        var installedPath = Path.Combine(modsPath, "examplemod.zip");
        await File.WriteAllTextAsync(installedPath, "installed");

        var installedMod = new InstalledMod(
            Identifier: "examplemod",
            Name: "Example Mod",
            Version: "1.0.0",
            Path: installedPath,
            FileName: "examplemod.zip",
            IsDirectory: false,
            Authors: Array.Empty<string>(),
            GameVersions: Array.Empty<string>(),
            Error: null);
        var update = new ModUpdateStatus(
            ModId: "examplemod",
            CurrentVersion: "1.0.0",
            Kind: ModUpdateKind.UpdateAvailable,
            AvailableVersion: "1.1.0",
            DownloadFileName: "examplemod-1.1.0.zip",
            DownloadUrl: "http://mods.vintagestory.at/files/examplemod-1.1.0.zip",
            ErrorCode: null,
            Message: "update available");

        var installer = new ModUpdateInstaller(new BackupService(), new HttpClient(new StaticResponseHandler(CreateZipWithMod("examplemod", "1.1.0"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallUpdateAsync(installedMod, update, modsPath));
    }

    private static byte[] CreateZipWithModId(string modId)
    {
        return CreateZipWithMod(modId, "1.1.0");
    }

    private static byte[] CreateZipWithMod(string modId, string version)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("modinfo.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
            writer.Write($"{{\"modid\":\"{modId}\",\"name\":\"{modId}\",\"version\":\"{version}\"}}");
        }

        return memory.ToArray();
    }

    private sealed class CdnRedirectResponseHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public CdnRedirectResponseHandler(byte[] payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(
                    HttpMethod.Get,
                    "https://moddbcdn.vintagestory.at/awearablelight-v1.2._bdeeb90989b76fc0599cfba9f2e2d497.zip?dl=awearablelight-v1.2.0.zip"),
                Content = new ByteArrayContent(_payload)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            response.Content.Headers.ContentLength = _payload.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public StaticResponseHandler(byte[] payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(_payload)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            response.Content.Headers.ContentLength = _payload.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class DynamicResponseHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public DynamicResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vintage-mod-updater-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}