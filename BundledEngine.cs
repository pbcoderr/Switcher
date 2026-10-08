using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Switcher
{
    public static class BundledEngine
    {
        public const string Version = "1.14.2";
        public static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Engine", "sing-box.exe"); } }
        public static void EnsureAvailable()
        {
            if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException("Эта сборка маршрутизации предназначена для 64-разрядной Windows.");
            string folder = Path.GetDirectoryName(FilePath);
            RejectLink(folder);
            Directory.CreateDirectory(folder);
            using (var resource = typeof(BundledEngine).Assembly.GetManifestResourceStream("Switcher.SingBox.zip"))
            {
                if (resource == null) throw new InvalidDataException("В этой сборке отсутствует движок маршрутизации. Установи полную сборку Switcher.");
                using (var archive = new ZipArchive(resource, ZipArchiveMode.Read))
                    foreach (string name in new[] { "sing-box.exe", "libcronet.dll" })
                    {
                        string destination = Path.Combine(folder, name);
                        RejectLink(destination);
                        string expected = name == "sing-box.exe" ? "7BBEF1DEA9189EE12799AE834EA4B4658355DA25C47A21AD8804904C0CCD9410" : "3217C6260FBCA5F16072E0B79735742F40109A63BB0FF88FD6B96DD6B54A2928";
                        if (File.Exists(destination) && Matches(destination, expected)) continue;
                        var entry = archive.GetEntry("sing-box-1.14.2-windows-amd64/" + name);
                        if (entry == null) throw new InvalidDataException("Архив встроенного движка повреждён.");
                        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            using (var input = entry.Open())
                            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) input.CopyTo(output);
                            Verify(temporary, expected);
                            if (File.Exists(destination)) File.Replace(temporary, destination, null, true); else File.Move(temporary, destination);
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
            }
            ValidateFiles(folder);
        }
        static void RejectLink(string path)
        {
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Путь встроенного движка не должен быть ссылкой: " + path);
        }
        public static void ValidateFiles(string folder)
        {
            if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException("Эта сборка маршрутизации предназначена для 64-разрядной Windows.");
            Verify(Path.Combine(folder, "sing-box.exe"), "7BBEF1DEA9189EE12799AE834EA4B4658355DA25C47A21AD8804904C0CCD9410");
            Verify(Path.Combine(folder, "libcronet.dll"), "3217C6260FBCA5F16072E0B79735742F40109A63BB0FF88FD6B96DD6B54A2928");
        }
        static void Verify(string file, string expected)
        {
            if (!File.Exists(file)) throw new FileNotFoundException("Комплектный движок не найден. Распакуй весь архив Switcher и повтори установку. Для сборки из исходников запусти Prepare-Engine.ps1.", file);
            if (!Matches(file, expected)) throw new InvalidDataException("Файл движка повреждён или заменён: " + Path.GetFileName(file) + ". Повтори установку из полного архива Switcher.");
        }
        static bool Matches(string file, string expected)
        {
            using (var stream = File.OpenRead(file))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") == expected;
        }
    }
}
