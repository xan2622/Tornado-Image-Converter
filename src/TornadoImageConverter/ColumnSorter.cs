namespace TornadoImageConverter;

/// <summary>How the content of a list column is compared.</summary>
internal enum SortKind
{
    /// <summary>Text, case-insensitive, with numbers compared by value (SIMU2 before SIMU10).</summary>
    Text,

    /// <summary>Number contained in the text ("1 234 B").</summary>
    Number
}

/// <summary>Compares two ListView rows on one column.</summary>
internal sealed class ColumnSorter(int column, SortKind kind, bool ascending) : System.Collections.IComparer
{
    public int Compare(object? x, object? y)
    {
        string a = Text(x), b = Text(y);
        int result = kind == SortKind.Number ? Digits(a).CompareTo(Digits(b)) : NaturalCompare(a, b);
        return ascending ? result : -result;
    }

    private string Text(object? item) =>
        item is ListViewItem { SubItems: var subItems } && column < subItems.Count ? subItems[column].Text : "";

    /// <summary>Number made of all the digits of the text ("1 234 B" : 1234, whatever the separators); -1 if there are none.</summary>
    private static long Digits(string text)
    {
        long result = 0;
        bool found = false;
        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
                continue;
            result = result * 10 + (c - '0');
            found = true;
        }
        return found ? result : -1;
    }

    private static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int startA = i, startB = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                    i++;
                while (j < b.Length && char.IsAsciiDigit(b[j]))
                    j++;

                string numberA = a[startA..i].TrimStart('0'), numberB = b[startB..j].TrimStart('0');
                int byLength = numberA.Length.CompareTo(numberB.Length);
                if (byLength != 0)
                    return byLength;
                int byValue = string.CompareOrdinal(numberA, numberB);
                if (byValue != 0)
                    return byValue;
            }
            else
            {
                int byChar = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (byChar != 0)
                    return byChar;
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
