using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using CodexHelper.Models;
using CodexHelper.ViewModels;

namespace CodexHelper.Rendering;

public static class ConversationDocumentBuilder
{
    private static readonly Brush PageText = BrushFromRgb(0x1F, 0x29, 0x37);
    private static readonly Brush MutedText = BrushFromRgb(0x6B, 0x72, 0x80);
    private static readonly Brush CodeBackground = BrushFromRgb(0xF3, 0xF4, 0xF6);
    private static readonly Brush UserBrush = BrushFromRgb(0x0F, 0x76, 0x66);
    private static readonly Brush AssistantBrush = BrushFromRgb(0x31, 0x4E, 0x9E);
    private static readonly Brush ToolBrush = BrushFromRgb(0x85, 0x4D, 0x0E);
    private static readonly Brush ErrorBrush = BrushFromRgb(0xB4, 0x23, 0x18);

    public static FlowDocument CreateDocument()
    {
        var document = new FlowDocument();
        ConfigureDocument(document);
        return document;
    }

    public static FlowDocument Create(MainViewModel viewModel)
    {
        var document = CreateDocument();
        RebuildConversation(document, viewModel, viewModel.ConversationMessages);
        return document;
    }

    public static void RebuildConversation(
        FlowDocument document,
        MainViewModel viewModel,
        IReadOnlyList<ConversationMessage> messages)
    {
        ResetDocument(document);
        if (TryAddConversationPlaceholder(document, viewModel, messages))
        {
            return;
        }

        foreach (var message in messages)
        {
            AddMessage(
                document,
                message,
                viewModel.GetRoleDisplayName(message),
                viewModel.FormatMessageTimestamp(message.Timestamp));
        }
    }

    public static bool TryAddConversationPlaceholder(
        FlowDocument document,
        MainViewModel viewModel,
        IReadOnlyList<ConversationMessage> messages)
    {
        if (viewModel.SelectedThread is null)
        {
            AddPlaceholder(document, viewModel.NoSessionText);
            return true;
        }

        if (messages.Count == 0)
        {
            AddPlaceholder(document, viewModel.NoMessagesText);
            return true;
        }

        return false;
    }

    public static void AddConversationMessage(
        FlowDocument document,
        ConversationMessage message,
        string roleText,
        string timestampText)
    {
        AddMessage(document, message, roleText, timestampText);
    }

    public static void AddNotice(FlowDocument document, string text)
    {
        document.Blocks.Add(new Paragraph(new Run(text))
        {
            Foreground = MutedText,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12)
        });
    }

    public static FlowDocument CreateText(string text, string placeholder)
    {
        var document = CreateDocument();
        RebuildText(document, text, placeholder);
        return document;
    }

    public static void RebuildText(FlowDocument document, string text, string placeholder)
    {
        ResetDocument(document);
        if (string.IsNullOrWhiteSpace(text))
        {
            AddPlaceholder(document, placeholder);
            return;
        }

        AddMarkdownishContent(document, text);
    }

    public static void Clear(FlowDocument document)
    {
        ResetDocument(document);
    }

    private static void ResetDocument(FlowDocument document)
    {
        document.Blocks.Clear();
        ConfigureDocument(document);
    }

    private static void ConfigureDocument(FlowDocument document)
    {
        document.PagePadding = new Thickness(22);
        document.FontFamily = new FontFamily("Segoe UI");
        document.FontSize = 13;
        document.Foreground = PageText;
        document.LineHeight = 20;
    }

    private static void AddPlaceholder(FlowDocument document, string text)
    {
        document.Blocks.Add(new Paragraph(new Run(text))
        {
            Foreground = MutedText,
            FontSize = 14,
            Margin = new Thickness(0, 8, 0, 0)
        });
    }

    private static void AddMessage(
        FlowDocument document,
        ConversationMessage message,
        string roleText,
        string timestampText)
    {
        var header = new Paragraph
        {
            Margin = new Thickness(0, 18, 0, 5)
        };

        header.Inlines.Add(new Run(roleText)
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushForKind(message.Kind)
        });

        if (!string.IsNullOrWhiteSpace(timestampText))
        {
            header.Inlines.Add(new Run($"  {timestampText}")
            {
                Foreground = MutedText,
                FontSize = 11
            });
        }

        document.Blocks.Add(header);
        AddMarkdownishContent(document, message.Content);
    }

    private static void AddMarkdownishContent(FlowDocument document, string content)
    {
        using var reader = new StringReader(content);
        var inCode = false;
        var codeBuilder = new StringBuilder();
        Paragraph? textParagraph = null;

        while (reader.ReadLine() is { } line)
        {
            if (IsCodeFence(line))
            {
                if (inCode)
                {
                    FlushText(document, ref textParagraph);
                    FlushCode(document, codeBuilder);
                    inCode = false;
                }
                else
                {
                    FlushText(document, ref textParagraph);
                    inCode = true;
                }

                continue;
            }

            if (inCode)
            {
                AppendCodeLine(codeBuilder, line);
            }
            else
            {
                AddTextLine(ref textParagraph, line);
            }
        }

        FlushText(document, ref textParagraph);
        FlushCode(document, codeBuilder);
    }

    private static bool IsCodeFence(string line)
    {
        return line.AsSpan().TrimStart().StartsWith("```".AsSpan(), StringComparison.Ordinal);
    }

    private static void AddTextLine(ref Paragraph? paragraph, string line)
    {
        paragraph ??= new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 8)
        };

        var trimmedEndLength = GetTrimmedEndLength(line);
        if (trimmedEndLength == 0)
        {
            paragraph.Inlines.Add(new LineBreak());
            return;
        }

        AddLine(paragraph, line, trimmedEndLength);
        paragraph.Inlines.Add(new LineBreak());
    }

    private static void FlushText(FlowDocument document, ref Paragraph? paragraph)
    {
        if (paragraph is null)
        {
            return;
        }

        document.Blocks.Add(paragraph);
        paragraph = null;
    }

    private static void AddLine(Paragraph paragraph, string line, int trimmedEndLength)
    {
        var span = line.AsSpan(0, trimmedEndLength);
        var trimmedStart = span.TrimStart();
        var headingLevel = 0;
        while (headingLevel < trimmedStart.Length && trimmedStart[headingLevel] == '#')
        {
            headingLevel++;
        }

        if (headingLevel is >= 1 and <= 4 &&
            trimmedStart.Length > headingLevel &&
            trimmedStart[headingLevel] == ' ')
        {
            paragraph.Inlines.Add(new Run(trimmedStart[(headingLevel + 1)..].ToString())
            {
                FontWeight = FontWeights.SemiBold,
                FontSize = headingLevel <= 2 ? 15 : 14
            });
            return;
        }

        paragraph.Inlines.Add(new Run(trimmedEndLength == line.Length ? line : span.ToString()));
    }

    private static void AppendCodeLine(StringBuilder builder, string line)
    {
        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(line);
    }

    private static void FlushCode(FlowDocument document, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        document.Blocks.Add(new Paragraph(new Run(builder.ToString()))
        {
            Margin = new Thickness(0, 2, 0, 10),
            Padding = new Thickness(10),
            Background = CodeBackground,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            LineHeight = 18
        });

        builder.Clear();
    }

    private static int GetTrimmedEndLength(string value)
    {
        var length = value.Length;
        while (length > 0 && char.IsWhiteSpace(value[length - 1]))
        {
            length--;
        }

        return length;
    }

    private static Brush BrushForKind(ConversationMessageKind kind)
    {
        return kind switch
        {
            ConversationMessageKind.User => UserBrush,
            ConversationMessageKind.Assistant => AssistantBrush,
            ConversationMessageKind.Tool or ConversationMessageKind.Reasoning => ToolBrush,
            ConversationMessageKind.Error => ErrorBrush,
            _ => MutedText
        };
    }

    private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
