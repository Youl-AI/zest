namespace LimeWin.Core.Office;

/// <summary>Word <c>WdSaveFormat</c> values Lime saves with.</summary>
public static class WordFormat
{
    public const int Doc97 = 0;        // wdFormatDocument97 (test fixtures only)
    public const int Rtf = 6;          // wdFormatRTF (test fixtures only)
    public const int UnicodeText = 7;  // wdFormatUnicodeText (UTF-16; rewritten as UTF-8)
    public const int FilteredHtml = 10; // wdFormatFilteredHTML
    public const int Docx = 16;        // wdFormatXMLDocument
    public const int Pdf = 17;         // wdFormatPDF
    public const int Odt = 23;         // wdFormatOpenDocumentText
}

/// <summary>Other Word constants.</summary>
public static class WordConst
{
    public const int DoNotSaveChanges = 0; // wdDoNotSaveChanges
    public const int AlertsNone = 0;       // wdAlertsNone

    /// <summary>A deliberately wrong open password: a protected document then fails with an error instead of a prompt.</summary>
    public const string WrongPassword = "\u0001lime";

    /// <summary>Word error number for "The password is incorrect" (HRESULT 0x800A1520).</summary>
    public const int PasswordIncorrectError = 0x1520;
}

/// <summary>Shared Office (Mso*) constants.</summary>
public static class MsoConst
{
    public const int AutomationSecurityForceDisable = 3; // msoAutomationSecurityForceDisable
    public const int EncodingUtf8 = 65001;               // msoEncodingUTF8
    public const int EncodingUnicodeLittleEndian = 1200; // msoEncodingUnicodeLittleEndian
}
