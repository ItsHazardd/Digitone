using System;
using System.Linq;

namespace Digitone
{
    internal static class ReleaseDate
    {
        internal static string Format(string value)
        {
            if (String.IsNullOrEmpty(value) || value.Any(c => !Char.IsDigit(c) && c != '/' && c != '-')) return value;
            string digits = new String(value.Where(Char.IsDigit).ToArray());
            if (digits.Length > 8) return value;
            return digits.Length <= 4 ? digits : digits.Length <= 6 ? digits.Substring(0,4) + "/" + digits.Substring(4) : digits.Substring(0,4) + "/" + digits.Substring(4,2) + "/" + digits.Substring(6);
        }
        internal static bool Valid(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return true;
            string formatted = Format(value.Trim()); string[] parts = formatted.Split('/');
            int year, month = 1, day = 1;
            if (parts.Length > 3 || parts[0].Length != 4 || !Int32.TryParse(parts[0],out year) || year < 1 || year > 9999) return false;
            if (parts.Length > 1 && (parts[1].Length != 2 || !Int32.TryParse(parts[1],out month) || month < 1 || month > 12)) return false;
            if (parts.Length > 2 && (parts[2].Length != 2 || !Int32.TryParse(parts[2],out day) || day < 1 || day > DateTime.DaysInMonth(year,month))) return false;
            return true;
        }
        internal static string Storage(string value) { return Format(value.Trim()).Replace('/','-'); }
    }
}
