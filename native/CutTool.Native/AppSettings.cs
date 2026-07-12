using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CutTool.Native
{
    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember(Name = "hotkey")] public string Hotkey { get; set; }
        [DataMember(Name = "saveDir")] public string SaveDirectory { get; set; }
        [DataMember(Name = "imageFormat")] public string ImageFormat { get; set; }
        [DataMember(Name = "autoCopy")] public bool AutoCopy { get; set; }
        [DataMember(Name = "showTranslateWindow")] public bool ShowTranslateWindow { get; set; }
        [DataMember(Name = "translateFrom")] public string TranslateFrom { get; set; }
        [DataMember(Name = "translateTo")] public string TranslateTo { get; set; }
        [DataMember(Name = "autoLaunch")] public bool AutoLaunch { get; set; }
        [DataMember(Name = "showTray")] public bool ShowTray { get; set; }

        internal static AppSettings CreateDefault()
        {
            return new AppSettings
            {
                Hotkey = "Ctrl+Shift+A",
                SaveDirectory = string.Empty,
                ImageFormat = "png",
                AutoCopy = true,
                ShowTranslateWindow = true,
                TranslateFrom = "en",
                TranslateTo = "zh-CN",
                AutoLaunch = false,
                ShowTray = true
            };
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            AppSettings defaults = CreateDefault();
            Hotkey = defaults.Hotkey;
            SaveDirectory = defaults.SaveDirectory;
            ImageFormat = defaults.ImageFormat;
            AutoCopy = defaults.AutoCopy;
            ShowTranslateWindow = defaults.ShowTranslateWindow;
            TranslateFrom = defaults.TranslateFrom;
            TranslateTo = defaults.TranslateTo;
            AutoLaunch = defaults.AutoLaunch;
            ShowTray = defaults.ShowTray;
        }

        internal void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Hotkey)) Hotkey = "Ctrl+Shift+A";
            if (SaveDirectory == null) SaveDirectory = string.Empty;
            if (!string.Equals(ImageFormat, "jpg", StringComparison.OrdinalIgnoreCase)) ImageFormat = "png";
            if (string.IsNullOrWhiteSpace(TranslateFrom)) TranslateFrom = "en";
            if (string.IsNullOrWhiteSpace(TranslateTo)) TranslateTo = "zh-CN";
        }

        internal AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }
    }

    internal sealed class SettingsStore
    {
        private readonly string filePath;

        internal SettingsStore()
        {
            filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "cuttool",
                "settings.json");
        }

        internal AppSettings Load()
        {
            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    var settings = (AppSettings)serializer.ReadObject(stream);
                    settings.Normalize();
                    return settings;
                }
            }
            catch
            {
                return AppSettings.CreateDefault();
            }
        }

        internal void Save(AppSettings settings)
        {
            settings.Normalize();
            string directory = Path.GetDirectoryName(filePath);
            Directory.CreateDirectory(directory);
            string temporary = filePath + ".tmp";
            using (var stream = File.Create(temporary))
            {
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                serializer.WriteObject(stream, settings);
            }
            if (File.Exists(filePath)) File.Replace(temporary, filePath, null);
            else File.Move(temporary, filePath);
        }
    }
}
