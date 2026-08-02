using System.Globalization;

namespace MultiChatManager2.Updates;

public readonly record struct SemanticVersion :
    IComparable<SemanticVersion>
{
    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public IReadOnlyList<string> Prerelease { get; }

    public string? BuildMetadata { get; }

    public bool IsPrerelease =>
        Prerelease.Count > 0;

    private SemanticVersion(
        int major,
        int minor,
        int patch,
        IReadOnlyList<string> prerelease,
        string? buildMetadata)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        BuildMetadata = buildMetadata;
    }

    public static SemanticVersion Parse(
        string value)
    {
        if (!TryParse(
                value,
                out SemanticVersion version))
        {
            throw new FormatException(
                $"无效的语义版本号：{value}");
        }

        return version;
    }

    public static bool TryParse(
        string? value,
        out SemanticVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized =
            value.Trim();

        if (normalized.StartsWith(
                "v",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized =
                normalized[1..];
        }

        string? buildMetadata = null;
        int plusIndex =
            normalized.IndexOf('+');

        if (plusIndex >= 0)
        {
            buildMetadata =
                normalized[(plusIndex + 1)..];

            normalized =
                normalized[..plusIndex];

            if (!IsValidIdentifierSequence(
                    buildMetadata,
                    allowLeadingZeroNumeric: true))
            {
                return false;
            }
        }

        IReadOnlyList<string> prerelease =
            Array.Empty<string>();

        int dashIndex =
            normalized.IndexOf('-');

        if (dashIndex >= 0)
        {
            string prereleaseText =
                normalized[(dashIndex + 1)..];

            normalized =
                normalized[..dashIndex];

            if (!IsValidIdentifierSequence(
                    prereleaseText,
                    allowLeadingZeroNumeric: false))
            {
                return false;
            }

            prerelease =
                prereleaseText.Split('.');
        }

        string[] core =
            normalized.Split('.');

        if (core.Length != 3 ||
            !TryParseCoreNumber(core[0], out int major) ||
            !TryParseCoreNumber(core[1], out int minor) ||
            !TryParseCoreNumber(core[2], out int patch))
        {
            return false;
        }

        version =
            new SemanticVersion(
                major,
                minor,
                patch,
                prerelease,
                buildMetadata);

        return true;
    }

    public int CompareTo(
        SemanticVersion other)
    {
        int result =
            Major.CompareTo(other.Major);

        if (result != 0)
        {
            return result;
        }

        result =
            Minor.CompareTo(other.Minor);

        if (result != 0)
        {
            return result;
        }

        result =
            Patch.CompareTo(other.Patch);

        if (result != 0)
        {
            return result;
        }

        if (!IsPrerelease &&
            !other.IsPrerelease)
        {
            return 0;
        }

        if (!IsPrerelease)
        {
            return 1;
        }

        if (!other.IsPrerelease)
        {
            return -1;
        }

        int count =
            Math.Max(
                Prerelease.Count,
                other.Prerelease.Count);

        for (int index = 0;
             index < count;
             index++)
        {
            if (index >= Prerelease.Count)
            {
                return -1;
            }

            if (index >= other.Prerelease.Count)
            {
                return 1;
            }

            string left =
                Prerelease[index];

            string right =
                other.Prerelease[index];

            bool leftNumeric =
                long.TryParse(
                    left,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long leftNumber);

            bool rightNumeric =
                long.TryParse(
                    right,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long rightNumber);

            if (leftNumeric &&
                rightNumeric)
            {
                result =
                    leftNumber.CompareTo(
                        rightNumber);
            }
            else if (leftNumeric)
            {
                result = -1;
            }
            else if (rightNumeric)
            {
                result = 1;
            }
            else
            {
                result =
                    string.CompareOrdinal(
                        left,
                        right);
            }

            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    public override string ToString()
    {
        string result =
            $"{Major}.{Minor}.{Patch}";

        if (Prerelease.Count > 0)
        {
            result +=
                "-" +
                string.Join(
                    ".",
                    Prerelease);
        }

        if (!string.IsNullOrWhiteSpace(
                BuildMetadata))
        {
            result +=
                "+" +
                BuildMetadata;
        }

        return result;
    }

    public static bool operator >(
        SemanticVersion left,
        SemanticVersion right) =>
        left.CompareTo(right) > 0;

    public static bool operator <(
        SemanticVersion left,
        SemanticVersion right) =>
        left.CompareTo(right) < 0;

    public static bool operator >=(
        SemanticVersion left,
        SemanticVersion right) =>
        left.CompareTo(right) >= 0;

    public static bool operator <=(
        SemanticVersion left,
        SemanticVersion right) =>
        left.CompareTo(right) <= 0;

    private static bool TryParseCoreNumber(
        string value,
        out int number)
    {
        number = 0;

        if (string.IsNullOrWhiteSpace(value) ||
            (value.Length > 1 &&
             value[0] == '0'))
        {
            return false;
        }

        return int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static bool IsValidIdentifierSequence(
        string value,
        bool allowLeadingZeroNumeric)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (string part in value.Split('.'))
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                return false;
            }

            if (part.Any(
                    character =>
                        !(char.IsAsciiLetterOrDigit(character) ||
                          character == '-')))
            {
                return false;
            }

            bool numeric =
                part.All(char.IsAsciiDigit);

            if (numeric &&
                !allowLeadingZeroNumeric &&
                part.Length > 1 &&
                part[0] == '0')
            {
                return false;
            }
        }

        return true;
    }
}
