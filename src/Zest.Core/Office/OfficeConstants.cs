namespace Zest.Core.Office;

/// <summary>Word <c>WdSaveFormat</c> values Zest saves with.</summary>
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
    public const int InlineShapeLinkedPicture = 4; // wdInlineShapeLinkedPicture

    /// <summary>A deliberately wrong open password: a protected document then fails with an error instead of a prompt.</summary>
    public const string WrongPassword = "\u0001lime";

    /// <summary>Word error number for "The password is incorrect" (HRESULT 0x800A1520).</summary>
    public const int PasswordIncorrectError = 0x1520;
}

/// <summary>PowerPoint <c>PpSaveAsFileType</c> values Zest saves with.</summary>
public static class PowerPointFormat
{
    public const int Ppt = 1;    // ppSaveAsPresentation (test fixtures only)
    public const int Pptx = 24;  // ppSaveAsOpenXMLPresentation
    public const int Pdf = 32;   // ppSaveAsPDF
    public const int Odp = 35;   // ppSaveAsOpenDocumentPresentation (test fixtures only)
}

/// <summary>Other PowerPoint constants.</summary>
public static class PowerPointConst
{
    public const int AlertsNone = 1; // ppAlertsNone (0 is not a valid PpAlertLevel)

    /// <summary>
    /// Appended to the file name: "path::password::" opens with that password, so with a deliberately wrong one a
    /// protected presentation fails with an error instead of a prompt (measured: plain files open normally).
    /// </summary>
    public const string WrongPasswordSuffix = "::\u0001lime::";

    /// <summary>What <c>Presentations.Open</c> fails with for a wrong password (E_FAIL, measured for pptx and ppt).</summary>
    public const int PasswordIncorrectHResult = unchecked((int)0x80004005);
}

/// <summary>Excel <c>XlFileFormat</c> values Zest saves with.</summary>
public static class ExcelFormat
{
    public const int UnicodeText = 42; // xlUnicodeText (UTF-16, tab separated; rewritten as UTF-8)
    public const int Xlsx = 51;        // xlOpenXMLWorkbook
    public const int Xls = 56;         // xlExcel8 (test fixtures only)
    public const int Ods = 60;         // xlOpenDocumentSpreadsheet
    public const int CsvUtf8 = 62;     // xlCSVUTF8 (written with a BOM; rewritten without)
}

/// <summary>Other Excel constants.</summary>
public static class ExcelConst
{
    public const int Delimited = 1;    // xlDelimited
    public const int TypePdf = 0;      // xlTypePDF

    /// <summary>A deliberately wrong open password: a protected workbook then fails with an error instead of a prompt.</summary>
    public const string WrongPassword = "\u0001lime";
}

/// <summary>Shared Office (Mso*) constants.</summary>
public static class MsoConst
{
    public const int False = 0;                          // msoFalse
    public const int True = -1;                          // msoTrue
    public const int AutomationSecurityForceDisable = 3; // msoAutomationSecurityForceDisable
    public const int EncodingUtf8 = 65001;               // msoEncodingUTF8
    public const int EncodingUnicodeLittleEndian = 1200; // msoEncodingUnicodeLittleEndian
}
