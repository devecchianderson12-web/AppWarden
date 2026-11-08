using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Serialization;

namespace CandadoApp
{
    public class BlockedApp
    {
        public string Name = "";      // texto que ve el usuario
        public string FileName = "";  // nombre del .exe en minúsculas, usado para comparar
        public string Path = "";      // ruta completa si se conoce

        public BlockedApp() { }

        public BlockedApp(string path, string name = null)
        {
            Path = path ?? "";
            FileName = System.IO.Path.GetFileName(Path).ToLowerInvariant();
            Name = string.IsNullOrEmpty(name) ? FileName : name;
        }
    }

    public class Settings
    {
        public List<BlockedApp> Blocked = new List<BlockedApp>();
        public bool ProtectionEnabled = true;
        public bool AllowClose = true;   // muestra la X para cerrar sin la contraseña
        public string LockImage = "";
        public string PasswordSalt = "";
        public string PasswordHash = "";
        public string PinSalt = "";
        public string PinHash = "";

        private const int HashIterations = 100000;

        public bool HasPassword
        {
            get { return PasswordHash.Length > 0; }
        }

        public bool HasPin
        {
            get { return PinHash.Length > 0; }
        }

        // La protección puede activarse con contraseña, con PIN, o con ambos.
        public bool HasCredential
        {
            get { return HasPassword || HasPin; }
        }

        public void SetPassword(string password)
        {
            byte[] salt = NewSalt();
            PasswordSalt = Convert.ToBase64String(salt);
            PasswordHash = Convert.ToBase64String(Derive(password, salt));
        }

        public void SetPin(string pin)
        {
            byte[] salt = NewSalt();
            PinSalt = Convert.ToBase64String(salt);
            PinHash = Convert.ToBase64String(Derive(pin, salt));
        }

        public void ClearPin()
        {
            PinSalt = "";
            PinHash = "";
        }

        public bool CheckPassword(string password)
        {
            return Matches(password, PasswordHash, PasswordSalt);
        }

        public bool CheckPin(string pin)
        {
            return Matches(pin, PinHash, PinSalt);
        }

        // Desbloquea con la contraseña o con el PIN, el que coincida.
        public bool Check(string value)
        {
            return CheckPassword(value) || CheckPin(value);
        }

        private static bool Matches(string value, string hash, string saltB64)
        {
            if (hash.Length == 0)
                return false;
            byte[] actual = Derive(value, Convert.FromBase64String(saltB64));
            byte[] expected = Convert.FromBase64String(hash);
            return actual.SequenceEqual(expected);
        }

        private static byte[] NewSalt()
        {
            byte[] salt = new byte[16];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                random.GetBytes(salt);
            return salt;
        }

        private static byte[] Derive(string password, byte[] salt)
        {
            using (Rfc2898DeriveBytes derive = new Rfc2898DeriveBytes(password, salt, HashIterations, HashAlgorithmName.SHA256))
                return derive.GetBytes(32);
        }

        public bool IsBlocked(string fileName)
        {
            return Blocked.Any(b => b.FileName == fileName);
        }

        private static string FilePath
        {
            get
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Program.Name);
                return Path.Combine(folder, "settings.xml");
            }
        }

        public static Settings Load()
        {
            try
            {
                using (FileStream stream = File.OpenRead(FilePath))
                    return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(stream);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidOperationException) { } // XML dañado: se usan los valores por defecto
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (FileStream stream = File.Create(FilePath))
                    new XmlSerializer(typeof(Settings)).Serialize(stream, this);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
