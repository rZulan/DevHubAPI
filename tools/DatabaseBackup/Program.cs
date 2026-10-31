using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;

if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Run on the existing Linux deployment host.");
const string keyPath = "/etc/devhub/backup-aes.key";
const UnixFileMode privateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
try
{
    if (!File.Exists(keyPath))
    {
        using var stream = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.None);
        File.SetUnixFileMode(keyPath, privateFile);
        stream.Write(RandomNumberGenerator.GetBytes(32));
    }
    File.SetUnixFileMode(keyPath, privateFile);
    var key = await File.ReadAllBytesAsync(keyPath);
    if (key.Length != 32) throw new InvalidOperationException("Invalid backup key.");
    if (args.Length == 3 && args[0] == "decrypt")
    {
        var encoded = await File.ReadAllBytesAsync(args[1]);
        if (encoded.Length < 28) throw new InvalidDataException("Invalid encrypted backup.");
        var plaintext = new byte[encoded.Length - 28];
        using var cipher = new AesGcm(key, 16);
        cipher.Decrypt(encoded.AsSpan(0, 12), encoded.AsSpan(28), encoded.AsSpan(12, 16), plaintext);
        using var destination = new FileStream(args[2], FileMode.CreateNew, FileAccess.Write, FileShare.None);
        File.SetUnixFileMode(args[2], privateFile);
        await destination.WriteAsync(plaintext);
        Console.WriteLine("Backup authenticated and decrypted to the specified private file.");
        return 0;
    }
    if (args.Length != 1 || !System.Text.RegularExpressions.Regex.IsMatch(args[0], "^[0-9]{14}-[a-f0-9]{8}$"))
        throw new ArgumentException("Provide the deployment release ID.");
    var directory = Path.Combine("/var/backups/devhub", args[0]);
    Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    var bacpac = Path.Combine(directory, "before-migration.bacpac");
    if (File.Exists(bacpac) || File.Exists(bacpac + ".aesgcm")) throw new InvalidOperationException("Backup already exists.");
    using var settings = JsonDocument.Parse(await File.ReadAllTextAsync("/etc/devhub/appsettings.Production.json"));
    var connection = new SqlConnectionStringBuilder(settings.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()) {ConnectTimeout = 90};
    var services = new DacServices(connection.ConnectionString);
    Console.WriteLine("Exporting a consistent pre-migration database backup while the API is stopped.");
    services.ExportBacpac(bacpac, connection.InitialCatalog, new DacExportOptions {CommandTimeout = 120}, null, null);
    File.SetUnixFileMode(bacpac, privateFile);
    using (var verified = BacPackage.Load(bacpac)) {}
    if (new FileInfo(bacpac).Length > 256 * 1024 * 1024) throw new InvalidOperationException("Backup exceeds the configured encryption memory budget.");
    var contents = await File.ReadAllBytesAsync(bacpac);
    var nonce = RandomNumberGenerator.GetBytes(12);
    var tag = new byte[16];
    var ciphertext = new byte[contents.Length];
    using (var cipher = new AesGcm(key, 16)) cipher.Encrypt(nonce, contents, ciphertext, tag);
    var encrypted = bacpac + ".aesgcm";
    using (var stream = new FileStream(encrypted, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    {
        File.SetUnixFileMode(encrypted, privateFile);
        await stream.WriteAsync(nonce); await stream.WriteAsync(tag); await stream.WriteAsync(ciphertext);
    }
    // Authenticate the saved ciphertext and compare the recovered archive before removing plaintext.
    var saved = await File.ReadAllBytesAsync(encrypted);
    var recovered = new byte[contents.Length];
    using (var cipher = new AesGcm(key, 16)) cipher.Decrypt(saved.AsSpan(0, 12), saved.AsSpan(28), saved.AsSpan(12, 16), recovered);
    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(contents), SHA256.HashData(recovered)))
        throw new CryptographicException("Backup verification failed.");
    File.Delete(bacpac);
    Console.WriteLine($"Verified encrypted database backup: {encrypted}");
    return 0;
}
catch (Exception error)
{
    // Do not print connection/provider exception text, which may contain sensitive metadata.
    Console.Error.WriteLine($"Database backup failed ({error.GetType().Name}); deployment must remain on the previous version.");
    return 1;
}
