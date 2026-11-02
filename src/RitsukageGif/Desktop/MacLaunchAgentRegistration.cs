using System.Xml;
using System.Xml.Linq;

namespace RitsukageGif.Desktop
{
    internal sealed class MacLaunchAgentRegistration(string userHome)
        : FileStartupRegistration(Path.Combine(userHome, "Library", "LaunchAgents", Label + ".plist"))
    {
        private const string Label = "local.ritsukagegif.login";

        public MacLaunchAgentRegistration() : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
        {
        }

        public override string CreateCommand(string executable)
        {
            var document = new XDocument(new("1.0", "UTF-8", null),
                new XElement("plist", new XAttribute("version", "1.0"),
                    new XElement("dict",
                        new XElement("key", "Label"), new XElement("string", Label),
                        new XElement("key", "ProgramArguments"),
                        new XElement("array", new XElement("string", Path.GetFullPath(executable)),
                            new XElement("string", "--serve")),
                        new XElement("key", "RunAtLoad"), new XElement("true"),
                        new XElement("key", "KeepAlive"), new XElement("false"),
                        new XElement("key", "LimitLoadToSessionType"), new XElement("string", "Aqua"))));
            return document.Declaration + "\n" + document + "\n";
        }

        public override bool IsEnabled(string executable)
        {
            if (Read() is not { } contents) return false;
            try
            {
                var dictionary = XDocument.Parse(contents).Root?.Element("dict");
                if (dictionary is null) return false;
                var values = new Dictionary<string, XElement>(StringComparer.Ordinal);
                foreach (var key in dictionary.Elements("key"))
                    if (key.ElementsAfterSelf().FirstOrDefault() is { } value)
                        values[key.Value] = value;
                return values.GetValueOrDefault("Label")?.Value == Label &&
                       values.GetValueOrDefault("RunAtLoad")?.Name == "true" &&
                       values.GetValueOrDefault("Disabled")?.Name != "true" &&
                       values.GetValueOrDefault("ProgramArguments") is { Name.LocalName: "array" } arguments &&
                       arguments.Elements("string").Select(item => item.Value)
                           .SequenceEqual([Path.GetFullPath(executable), "--serve"], StringComparer.Ordinal);
            }
            catch (XmlException)
            {
                return false;
            }
        }
    }
}
