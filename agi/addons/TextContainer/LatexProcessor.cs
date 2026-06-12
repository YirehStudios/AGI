using Godot;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public static class LatexProcessor
{
    private static readonly Regex TexRegex = new(@"\$\$([\s\S]+?)\$\$|\\\[([\s\S]+?)\\\]|\\\(([\s\S]+?)\\\)|\$([^\$]+?)\$", RegexOptions.Compiled);
    public static Dictionary<int, Texture2D> TextureCache = new Dictionary<int, Texture2D>();
    private static int _latexIdCounter = 0;

    public static void ClearCache()
    {
        TextureCache.Clear();
        _latexIdCounter = 0;
    }

    public static string ProcessText(string text)
    {
        string processedText = text;

        while (true)
        {
            Match match = TexRegex.Match(processedText);
            if (!match.Success)
            {
                break;
            }

            int start = match.Index;
            int length = match.Length;
            string latexExpr = "";
            for (int i = 1; i <= 4; i++)
            {
                if (!string.IsNullOrEmpty(match.Groups[i].Value))
                {
                    latexExpr = match.Groups[i].Value;
                    break;
                }
            }

            bool esOscuro = true;
            if (Logic.UI.ThemeManager.Instance != null)
            {
                esOscuro = Logic.UI.ThemeManager.Instance.EsModoOscuro;
            }
            Color mathColor = esOscuro ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.15f, 0.15f, 0.15f);

            var texNode = new LaTeXture
            {
                LatexExpression = latexExpr.Trim(),
                FontSize = 24.0f,
                MathColor = mathColor
            };

            var imgTex = texNode.Render();
            if (imgTex != null)
            {
                _latexIdCounter++;
                TextureCache[_latexIdCounter] = imgTex;
                processedText = processedText.Remove(start, length).Insert(start, $"[mathimg]{_latexIdCounter}[/mathimg]");
            }
            else
            {
                processedText = processedText.Remove(start, length).Insert(start, latexExpr);
            }
        }

        return processedText;
    }
}
