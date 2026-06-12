using Godot;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class MarkdownProcessor
{
    private TextContainer _container;
    private string _convertedText = "";
    private int _indentLevel = -1;
    private List<int> _indentSpaces = new List<int>();
    private List<string> _indentTypes = new List<string>();
    private int _currentParagraph = 0;
    private Dictionary<string, int> _headerAnchorParagraph = new Dictionary<string, int>();
    private Dictionary<string, int> _headerAnchorCount = new Dictionary<string, int>();
    private bool _withinTable = false;
    private int _tableRow = -1;
    private bool _skipLineBreak = false;
    private int _checkboxId = 0;
    private int _currentLine = 0;
    
    public Dictionary<int, int> CheckboxRecord = new Dictionary<int, int>();

    private Dictionary<char, int> _escapedCharactersMap = new Dictionary<char, int>();
    private const string EscapePlaceholder = "\uE000{0}\uE001";
    private const string EscapableCharacters = "\\*_~`[]()\"<>#-+.!";

    public MarkdownProcessor(TextContainer container)
    {
        _container = container;
    }

    public string Process(string sourceText)
    {
        if (!_container.BbcodeEnabled)
        {
            GD.PushWarning("WARNING: TextContainer node will not format Markdown syntax if it doesn't have 'bbcode_enabled=true'");
            return sourceText;
        }

        _convertedText = "";
        string preprocessedText = LatexProcessor.ProcessText(sourceText);
        string[] lines = preprocessedText.Replace("\r", "").Split('\n');
        _currentLine = 0;
        _indentLevel = -1;
        _indentSpaces.Clear();
        _indentTypes.Clear();
        bool withinBacktickBlock = false;
        bool withinTildeBlock = false;
        bool withinCodeBlock = false;
        int currentCodeBlockCharCount = 0;
        _withinTable = false;
        _tableRow = -1;
        _skipLineBreak = false;
        _checkboxId = 0;
        CheckboxRecord.Clear();
        _headerAnchorCount.Clear();
        _headerAnchorParagraph.Clear();

        foreach (string rawLine in lines)
        {
            string line = rawLine;
            withinCodeBlock = withinTildeBlock || withinBacktickBlock;
            if (_currentLine > 0 && !_skipLineBreak)
            {
                _convertedText += "\n";
                _currentParagraph++;
            }
            _skipLineBreak = false;
            _currentLine++;

            line = PreprocessLine(line);

            int backtickCount = CountFencedCodeBlockChars(line, '`');
            int tildeCount = CountFencedCodeBlockChars(line, '~');

            if (!withinTildeBlock && backtickCount >= 3)
            {
                if (withinBacktickBlock)
                {
                    if (backtickCount >= currentCodeBlockCharCount)
                    {
                        _convertedText = _convertedText.TrimEnd('\n');
                        _currentParagraph--;
                        _convertedText += "[/code]";
                        withinBacktickBlock = false;
                        continue;
                    }
                }
                else
                {
                    _convertedText += "[code]";
                    withinBacktickBlock = true;
                    currentCodeBlockCharCount = backtickCount;
                    continue;
                }
            }
            else if (!withinBacktickBlock && tildeCount >= 3)
            {
                if (withinTildeBlock)
                {
                    if (tildeCount >= currentCodeBlockCharCount)
                    {
                        _convertedText = _convertedText.TrimEnd('\n');
                        _currentParagraph--;
                        _convertedText += "[/code]";
                        withinTildeBlock = false;
                        continue;
                    }
                }
                else
                {
                    _convertedText += "[code]";
                    withinTildeBlock = true;
                    currentCodeBlockCharCount = tildeCount;
                    continue;
                }
            }

            if (withinCodeBlock)
            {
                _convertedText += EscapeBbcode(line);
                continue;
            }

            string processedLine = line;
            processedLine = ProcessEscapedCharacters(processedLine);

            processedLine = ProcessTableSyntax(processedLine);
            processedLine = ProcessListSyntax(processedLine, _indentSpaces, _indentTypes);
            processedLine = ProcessInlineCodeSyntax(processedLine);
            processedLine = ProcessImageSyntax(processedLine);
            processedLine = ProcessLinkSyntax(processedLine);
            processedLine = ProcessHrSyntax(processedLine);
            processedLine = ProcessTextFormattingSyntax(processedLine);
            processedLine = ProcessHeaderSyntax(processedLine);
            processedLine = ProcessCustomSyntax(processedLine);

            processedLine = ResetEscapedChars(processedLine);

            _convertedText += processedLine;
        }

        for (int i = _indentLevel; i >= 0; i--)
        {
            _convertedText += $"[/{_indentTypes[i]}]";
        }
        if (_withinTable)
        {
            _convertedText += "\n[/table]";
        }
        if (withinBacktickBlock || withinTildeBlock)
        {
            _convertedText = _convertedText.TrimEnd('\n');
            _convertedText += "[/code]";
        }

        return _convertedText;
    }

    private string PreprocessLine(string line) => line;

    private string ProcessCustomSyntax(string line)
    {
        string processedLine = line;

        // Custom [file] syntax
        var fileRegex = new Regex(@"\[file\](.*?)\[\/file\]");
        processedLine = fileRegex.Replace(processedLine, match =>
        {
            string filepath = match.Groups[1].Value;
            string filename = System.IO.Path.GetFileName(filepath);
            string ext = System.IO.Path.GetExtension(filepath).ToLower();
            
            bool esOscuro = true;
            if (Logic.UI.ThemeManager.Instance != null)
            {
                esOscuro = Logic.UI.ThemeManager.Instance.EsModoOscuro;
            }

            string iconPath = "res://Resources/Images/Icons/Util/files2.svg";
            string iconColor = "white";

            if (ext == ".cs") { iconColor = "#23a31c"; }
            else if (ext == ".py") { iconColor = "#3572A5"; }
            else if (ext == ".gd" || ext == ".tscn") { iconColor = "#478cbf"; }
            else if (ext == ".json") { iconColor = "#e6cc2e"; }
            else if (ext == ".txt" || ext == ".md" || ext == ".csv" || ext == ".log") { iconColor = "#a9b2c3"; }
            else if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp") { iconColor = "#e34f8c"; }
            else if (ext == ".mp4" || ext == ".webm" || ext == ".mkv") { iconColor = "#b44fe3"; }

            string textColor = esOscuro ? "white" : "black";
            return $" [img width=16 height=16 color=\"{iconColor}\"]{iconPath}[/img] [color={textColor}][b]{filename}[/b][/color] ";
        });

        return processedLine;
    }

    private string ProcessListSyntax(string line, List<int> indentSpaces, List<string> indentTypes)
    {
        string processedLine = "";
        if (line.Length == 0 && _indentLevel >= 0)
        {
            for (int i = _indentLevel; i >= 0; i--)
            {
                _convertedText += $"[/{indentTypes[_indentLevel]}]";
                _indentLevel--;
                indentSpaces.RemoveAt(indentSpaces.Count - 1);
                indentTypes.RemoveAt(indentTypes.Count - 1);
            }
            _convertedText += "\n";
            return "";
        }

        if (_indentLevel == -1)
        {
            if (line.Length > 2 && (line[0] == '-' || line[0] == '*' || line[0] == '+') && line[1] == ' ')
            {
                _indentLevel = 0;
                indentSpaces.Add(0);
                indentTypes.Add("ul");
                _convertedText += "[ul]";
                processedLine = line.Substring(2);
                processedLine = ProcessTaskListItem(processedLine);
            }
            else if (line.Length > 3 && line[0] == '1' && line[1] == '.' && line[2] == ' ')
            {
                _indentLevel = 0;
                indentSpaces.Add(0);
                indentTypes.Add("ol");
                _convertedText += "[ol]";
                processedLine = line.Substring(3);
            }
            else
            {
                processedLine = line;
            }
            return processedLine;
        }

        int nS = 0;
        foreach (char c in line)
        {
            if (c == ' ' || c == '\t')
            {
                nS++;
                continue;
            }
            else if (c == '-' || c == '*' || c == '+')
            {
                if (line.Length > nS + 2 && line[nS + 1] == ' ')
                {
                    if (nS == indentSpaces[_indentLevel])
                    {
                        processedLine = line.Substring(nS + 2);
                        processedLine = ProcessTaskListItem(processedLine);
                        break;
                    }
                    else if (nS > indentSpaces[_indentLevel])
                    {
                        _indentLevel++;
                        indentSpaces.Add(nS);
                        indentTypes.Add("ul");
                        _convertedText += "[ul]";
                        processedLine = line.Substring(nS + 2);
                        processedLine = ProcessTaskListItem(processedLine);
                        break;
                    }
                    else
                    {
                        for (int i = _indentLevel; i >= 0; i--)
                        {
                            if (nS < indentSpaces[i])
                            {
                                _convertedText += $"[/{indentTypes[_indentLevel]}]";
                                _indentLevel--;
                                indentSpaces.RemoveAt(indentSpaces.Count - 1);
                                indentTypes.RemoveAt(indentTypes.Count - 1);
                            }
                            else
                            {
                                break;
                            }
                        }
                        _convertedText += "\n";
                        processedLine = line.Substring(nS + 2);
                        processedLine = ProcessTaskListItem(processedLine);
                        break;
                    }
                }
            }
            else if (char.IsDigit(c))
            {
                if (line.Length > nS + 3 && line[nS + 1] == '.' && line[nS + 2] == ' ')
                {
                    if (nS == indentSpaces[_indentLevel])
                    {
                        processedLine = line.Substring(nS + 3);
                        break;
                    }
                    else if (nS > indentSpaces[_indentLevel])
                    {
                        _indentLevel++;
                        indentSpaces.Add(nS);
                        indentTypes.Add("ol");
                        _convertedText += "[ol]";
                        processedLine = line.Substring(nS + 3);
                        break;
                    }
                    else
                    {
                        for (int i = _indentLevel; i >= 0; i--)
                        {
                            if (nS < indentSpaces[i])
                            {
                                _convertedText += $"[/{indentTypes[_indentLevel]}]";
                                _indentLevel--;
                                indentSpaces.RemoveAt(indentSpaces.Count - 1);
                                indentTypes.RemoveAt(indentTypes.Count - 1);
                            }
                            else
                            {
                                break;
                            }
                        }
                        _convertedText += "\n";
                        processedLine = line.Substring(nS + 3);
                        break;
                    }
                }
            }

            // Not a list marker
            break;
        }

        if (string.IsNullOrEmpty(processedLine))
        {
            for (int i = _indentLevel; i >= 0; i--)
            {
                _convertedText += $"[/{indentTypes[i]}]";
                _indentLevel--;
                indentSpaces.RemoveAt(indentSpaces.Count - 1);
                indentTypes.RemoveAt(indentTypes.Count - 1);
            }
            _convertedText += "\n";
            processedLine = line;
        }

        return processedLine;
    }

    private string ProcessTaskListItem(string item)
    {
        if (item.Length <= 3 || item[0] != '[' || item[2] != ']' || item[3] != ' ' || (item[1] != ' ' && item[1] != 'x'))
        {
            return item;
        }

        string processedItem = item.Substring(4);
        string checkbox = "";
        
        var meta = new Godot.Collections.Dictionary();
        meta["markdownlabel-checkbox"] = true;
        meta["id"] = _checkboxId;
        
        CheckboxRecord[_checkboxId] = _currentLine - 1;
        _checkboxId++;

        if (item[1] == ' ')
        {
            checkbox = _container.UncheckedItemCharacter;
            meta["checked"] = false;
        }
        else if (item[1] == 'x')
        {
            checkbox = _container.CheckedItemCharacter;
            meta["checked"] = true;
        }

        if (_container.EnableCheckboxClicks)
        {
            processedItem = $"[url={Json.Stringify(meta)}]{checkbox}[/url]{processedItem}";
        }
        else
        {
            processedItem = $"{checkbox}{processedItem}";
        }

        return processedItem;
    }

    private string ProcessInlineCodeSyntax(string line)
    {
        var regex = new Regex(@"(`+)(.*?)(?:\1|$)");
        return regex.Replace(line, match => 
        {
            string unescapedContent = ResetEscapedChars(match.Groups[2].Value, true);
            unescapedContent = EscapeBbcode(unescapedContent);
            unescapedContent = EscapeChars(unescapedContent);
            return $"[code]{unescapedContent}[/code]";
        });
    }

    private string ProcessImageSyntax(string line)
    {
        string processedLine = line;
        var fullRegex = new Regex(@"\!\[(.*?)\]\((.*?)\)");
        var titleRegex = new Regex(@"\""(.*?)\""");

        processedLine = fullRegex.Replace(processedLine, match => 
        {
            string altText = match.Groups[1].Value;
            string urlStr = match.Groups[2].Value;
            
            Match titleResult = titleRegex.Match(urlStr);
            string title = "";
            if (titleResult.Success)
            {
                title = titleResult.Groups[1].Value;
                urlStr = urlStr.TrimEnd().Substring(0, urlStr.Length - titleResult.Value.Length).TrimEnd();
            }

            urlStr = EscapeChars(urlStr);
            return $"[img{(string.IsNullOrEmpty(altText) ? "" : $" alt=\"{altText}\"")}{(string.IsNullOrEmpty(title) ? "" : $" tooltip=\"{title}\"")}]{urlStr}[/img]";
        });
        
        return processedLine;
    }

    private string ProcessLinkSyntax(string line)
    {
        string processedLine = line;
        var fullRegex = new Regex(@"\[(.*?)\]\((.*?)\)");
        var titleRegex = new Regex(@"\""(.*?)\""");

        processedLine = fullRegex.Replace(processedLine, match => 
        {
            string textStr = match.Groups[1].Value;
            string urlStr = match.Groups[2].Value;
            
            Match titleResult = titleRegex.Match(urlStr);
            string title = "";
            if (titleResult.Success)
            {
                title = titleResult.Groups[1].Value;
                urlStr = urlStr.TrimEnd().Substring(0, urlStr.Length - titleResult.Value.Length).TrimEnd();
            }

            urlStr = EscapeChars(urlStr);
            
            string inserted = $"[url={urlStr}]{textStr}[/url]";
            if (!string.IsNullOrEmpty(title))
            {
                inserted = $"[hint={title}]{inserted}[/hint]";
            }
            return inserted;
        });

        var explicitRegex = new Regex(@"\<(.*?)\>");
        var mailRegex = new Regex(@"^\s*?([^\s]+\@[^\s]+\.[^\s]+)\s*?$");

        processedLine = explicitRegex.Replace(processedLine, match => 
        {
            string urlStr = match.Groups[1].Value;
            Match mailMatch = mailRegex.Match(urlStr);
            if (mailMatch.Success)
            {
                urlStr = mailMatch.Groups[1].Value;
                urlStr = EscapeChars(urlStr);
                return $"[url=mailto:{urlStr}]{urlStr}[/url]";
            }
            else
            {
                urlStr = EscapeChars(urlStr);
                return $"[url]{urlStr}[/url]";
            }
        });

        return processedLine;
    }

    private string ProcessTextFormattingSyntax(string line)
    {
        string processedLine = line;

        // Bold text
        var boldRegex = new Regex(@"(\*\*|__)(.*?)(?:\1|$)");
        processedLine = boldRegex.Replace(processedLine, match => $"[b]{match.Groups[2].Value}[/b]");

        // Italic text
        var italicRegex = new Regex(@"(\*|_)(.*?)(?:\1|$)");
        processedLine = italicRegex.Replace(processedLine, match => 
        {
            string resultStr = match.Groups[2].Value;
            bool openB = resultStr.StartsWith("[b]") && !resultStr.Contains("[/b]");
            bool closeB = resultStr.EndsWith("[/b]") && !resultStr.Contains("[b]");

            if (openB)
            {
                return $"[b][i]{resultStr.Substring(3)}[/i]";
            }
            else if (closeB)
            {
                return $"[i]{resultStr.Substring(0, resultStr.Length - 4)}[/i][/b]";
            }
            else
            {
                return $"[i]{resultStr}[/i]";
            }
        });

        // Strike-through
        var strikeRegex = new Regex(@"(\~\~)(.*?)(?:\1|$)");
        processedLine = strikeRegex.Replace(processedLine, match => $"[s]{match.Groups[2].Value}[/s]");

        return processedLine;
    }

    private string ProcessHeaderSyntax(string line)
    {
        string processedLine = line;
        var headerRegex = new Regex(@"^#+\s*[^\s].*");

        processedLine = headerRegex.Replace(processedLine, match => 
        {
            int n = 0;
            foreach (char c in match.Value)
            {
                if (c != '#' || n == 6) break;
                n++;
            }

            int nSpaces = 0;
            foreach (char c in match.Value.Substring(n))
            {
                if (c != ' ') break;
                nSpaces++;
            }

            HeaderFormat format = _container.GetHeaderFormat(n);
            string content = match.Value.Substring(n + nSpaces);
            
            string openingTags = GetHeaderTags(format, false);
            string closingTags = GetHeaderTags(format, true);
            
            string reference = GetHeaderReference(match.Value);
            _headerAnchorParagraph[reference] = _currentParagraph;

            string hrLine = "";
            if (format != null && format.DrawHorizontalRule)
            {
                hrLine = $"\n[hr height={_container.HrHeight} width={_container.HrWidth}% align=left color={_container.HrColor.ToHtml()}]";
            }
            
            return $"{openingTags}{content}{closingTags}{hrLine}";
        });

        return processedLine;
    }

    private string ProcessHrSyntax(string line)
    {
        string processedLine = line;
        var hrRegex = new Regex(@"^[ ]{0,3}([\-_*])\1{2,}\s*$");
        if (hrRegex.IsMatch(processedLine))
        {
            processedLine = $"[hr height={_container.HrHeight} width={_container.HrWidth}% align={_container.HrAlignment} color={_container.HrColor.ToHtml()}]";
        }
        return processedLine;
    }

    private string ProcessTableSyntax(string line)
    {
        if (line.Split('|').Length - 1 < 2)
        {
            if (_withinTable)
            {
                _withinTable = false;
                return "[/table]\n" + line;
            }
            return line;
        }

        _tableRow++;
        string[] splitLine = line.TrimStart('|').TrimEnd('|').Split('|');
        string processedLine = "";

        if (!_withinTable)
        {
            processedLine += $"[table={splitLine.Length}]\n";
            _withinTable = true;
        }
        else if (_tableRow == 1)
        {
            bool isDelimiter = true;
            foreach (string cell in splitLine)
            {
                string stripped = cell.Trim();
                int count = 0;
                foreach (char c in stripped) if (c == '-' || c == ':') count++;
                if (count != stripped.Length)
                {
                    isDelimiter = false;
                    break;
                }
            }
            if (isDelimiter)
            {
                _skipLineBreak = true;
                return "";
            }
        }

        foreach (string cell in splitLine)
        {
            processedLine += $"[cell]{cell.Trim()}[/cell]";
        }
        return processedLine;
    }

    private string EscapeBbcode(string source)
    {
        return source.Replace("[", "\x00").Replace("]", "[rb]").Replace("\x00", "[lb]");
    }

    private string EscapeChars(string text)
    {
        string escapedText = text;
        foreach (char c in EscapableCharacters)
        {
            if (!_escapedCharactersMap.ContainsKey(c))
                _escapedCharactersMap[c] = _escapedCharactersMap.Count;
            escapedText = escapedText.Replace(c.ToString(), string.Format(EscapePlaceholder, _escapedCharactersMap[c]));
        }
        return escapedText;
    }

    private string ResetEscapedChars(string text, bool code = false)
    {
        string unescapedText = text;
        foreach (char c in EscapableCharacters)
        {
            if (!_escapedCharactersMap.ContainsKey(c)) continue;
            unescapedText = unescapedText.Replace(string.Format(EscapePlaceholder, _escapedCharactersMap[c]), (code ? "\\" : "") + c);
        }
        return unescapedText;
    }

    private string ProcessEscapedCharacters(string line)
    {
        string pattern = @"\\([" + Regex.Escape(EscapableCharacters) + @"])";
        return Regex.Replace(line, pattern, match =>
        {
            char escapedChar = match.Groups[1].Value[0];
            if (!_escapedCharactersMap.ContainsKey(escapedChar))
                _escapedCharactersMap[escapedChar] = _escapedCharactersMap.Count;
            return string.Format(EscapePlaceholder, _escapedCharactersMap[escapedChar]);
        });
    }

    private int CountFencedCodeBlockChars(string line, char character)
    {
        string stripped = line.TrimStart();
        int count = 0;
        foreach (char c in stripped)
        {
            if (c == character) count++;
            else break;
        }
        return count;
    }

    private string GetHeaderTags(HeaderFormat format, bool closing)
    {
        if (format == null) return "";
        string tags = "";
        if (closing)
        {
            if (format.IsUnderlined) tags += "[/u]";
            if (format.IsItalic) tags += "[/i]";
            if (format.IsBold) tags += "[/b]";
            if (format.FontSize > 0) tags += "[/font_size]";
            if (format.OverrideFontColor) tags += "[/color]";
        }
        else
        {
            if (format.OverrideFontColor) tags += $"[color=#{format.FontColor.ToHtml()}]";
            if (format.FontSize > 0) tags += $"[font_size={(int)(format.FontSize * _container.GetThemeFontSize("normal_font_size"))}]";
            if (format.IsBold) tags += "[b]";
            if (format.IsItalic) tags += "[i]";
            if (format.IsUnderlined) tags += "[u]";
        }
        return tags;
    }

    private string GetHeaderReference(string headerString)
    {
        string anchor = "#" + headerString.TrimStart('#').Trim().ToLower().Replace(" ", "-");
        if (_headerAnchorCount.ContainsKey(anchor))
        {
            _headerAnchorCount[anchor]++;
            anchor += "-" + (_headerAnchorCount[anchor] - 1);
        }
        else
        {
            _headerAnchorCount[anchor] = 1;
        }
        return anchor;
    }
}
