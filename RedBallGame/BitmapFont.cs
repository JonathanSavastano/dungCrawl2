using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RedBallGame;

/// <summary>
/// Tiny built-in 5x7 bitmap font so we can show text without the MonoGame
/// content pipeline (which would need an .spritefont + mgcb build step).
/// Each glyph is drawn as rectangles from a generated texture, so it's fully
/// self-contained. Only uppercase letters are available.
/// </summary>
public class BitmapFont
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;

    private static readonly Dictionary<char, int[]> Glyphs = new()
    {
        ['A'] = new[] { 14, 17, 17, 31, 17, 17, 17 },
        ['B'] = new[] { 30, 17, 17, 30, 17, 17, 30 },
        ['C'] = new[] { 14, 17, 16, 16, 16, 17, 14 },
        ['D'] = new[] { 30, 17, 17, 17, 17, 17, 30 },
        ['E'] = new[] { 31, 16, 16, 30, 16, 16, 31 },
        ['F'] = new[] { 31, 16, 16, 30, 16, 16, 16 },
        ['G'] = new[] { 14, 17, 16, 23, 17, 17, 15 },
        ['H'] = new[] { 17, 17, 17, 31, 17, 17, 17 },
        ['I'] = new[] { 14, 4, 4, 4, 4, 4, 14 },
        ['J'] = new[] { 7, 2, 2, 2, 2, 18, 12 },
        ['K'] = new[] { 17, 18, 20, 24, 20, 18, 17 },
        ['L'] = new[] { 16, 16, 16, 16, 16, 16, 31 },
        ['M'] = new[] { 17, 27, 21, 21, 17, 17, 17 },
        ['N'] = new[] { 17, 25, 21, 19, 17, 17, 17 },
        ['O'] = new[] { 14, 17, 17, 17, 17, 17, 14 },
        ['P'] = new[] { 30, 17, 17, 30, 16, 16, 16 },
        ['Q'] = new[] { 14, 17, 17, 17, 21, 18, 13 },
        ['R'] = new[] { 30, 17, 17, 30, 20, 18, 17 },
        ['S'] = new[] { 15, 16, 16, 14, 1, 1, 30 },
        ['T'] = new[] { 31, 4, 4, 4, 4, 4, 4 },
        ['U'] = new[] { 17, 17, 17, 17, 17, 17, 14 },
        ['V'] = new[] { 17, 17, 17, 17, 17, 10, 4 },
        ['W'] = new[] { 17, 17, 17, 21, 21, 21, 10 },
        ['X'] = new[] { 17, 17, 10, 4, 10, 17, 17 },
        ['Y'] = new[] { 17, 17, 10, 4, 4, 4, 4 },
        ['Z'] = new[] { 31, 1, 2, 4, 8, 16, 31 },
        ['0'] = new[] { 14, 17, 19, 21, 25, 17, 14 },
        ['1'] = new[] { 4, 12, 4, 4, 4, 4, 14 },
        ['2'] = new[] { 14, 17, 1, 2, 4, 8, 31 },
        ['3'] = new[] { 30, 1, 1, 14, 1, 1, 30 },
        ['4'] = new[] { 2, 6, 10, 18, 31, 2, 2 },
        ['5'] = new[] { 31, 16, 30, 1, 1, 17, 14 },
        ['6'] = new[] { 14, 16, 16, 30, 17, 17, 14 },
        ['7'] = new[] { 31, 1, 2, 4, 8, 8, 8 },
        ['8'] = new[] { 14, 17, 17, 14, 17, 17, 14 },
        ['9'] = new[] { 14, 17, 17, 15, 1, 1, 14 },
        ['!'] = new[] { 4, 4, 4, 4, 4, 0, 4 },
        ['?'] = new[] { 14, 17, 1, 2, 4, 0, 4 },
        ['.'] = new[] { 0, 0, 0, 0, 0, 12, 12 },
        [','] = new[] { 0, 0, 0, 0, 12, 4, 8 },
        [':'] = new[] { 0, 12, 12, 0, 12, 12, 0 },
        ['-'] = new[] { 0, 0, 0, 31, 0, 0, 0 },
        ['+'] = new[] { 0, 4, 4, 31, 4, 4, 0 },
        ['/'] = new[] { 1, 2, 2, 4, 8, 8, 16 },
        ['\''] = new[] { 0, 4, 4, 8, 0, 0, 0 },
        ['('] = new[] { 2, 4, 8, 8, 8, 4, 2 },
        [')'] = new[] { 8, 4, 2, 2, 2, 4, 8 },
        ['='] = new[] { 0, 0, 31, 0, 31, 0, 0 },
        ['<'] = new[] { 2, 4, 8, 16, 8, 4, 2 },
        ['>'] = new[] { 8, 4, 2, 1, 2, 4, 8 },
        ['_'] = new[] { 0, 0, 0, 0, 0, 0, 31 },
    };

    private readonly GraphicsDevice _device;
    private readonly Dictionary<char, Texture2D> _textures = new();

    public BitmapFont(GraphicsDevice device)
    {
        _device = device;
    }

    public float Measure(string text, float scale) => text.Length * (GlyphWidth + 1) * scale;

    public void Draw(SpriteBatch spriteBatch, string text, float x, float y, float scale, Color color)
    {
        foreach (char raw in text)
        {
            char c = raw == ' ' ? ' ' : char.ToUpper(raw);
            if (c == ' ')
            {
                x += (GlyphWidth + 1) * scale;
                continue;
            }

            var texture = GlyphTexture(c);
            int w = (int)(GlyphWidth * scale);
            int h = (int)(GlyphHeight * scale);
            spriteBatch.Draw(texture, new Rectangle((int)x, (int)y, w, h), color);
            x += (GlyphWidth + 1) * scale;
        }
    }

    private Texture2D GlyphTexture(char c)
    {
        if (_textures.TryGetValue(c, out var cached))
        {
            return cached;
        }

        if (!Glyphs.TryGetValue(c, out var rows))
        {
            rows = Glyphs['?'];
        }

        var texture = new Texture2D(_device, GlyphWidth, GlyphHeight);
        var data = new Color[GlyphWidth * GlyphHeight];
        for (int y = 0; y < GlyphHeight; y++)
        {
            for (int x = 0; x < GlyphWidth; x++)
            {
                bool on = (rows[y] & (16 >> x)) != 0;
                data[y * GlyphWidth + x] = on ? Color.White : Color.Transparent;
            }
        }

        texture.SetData(data);
        _textures[c] = texture;
        return texture;
    }
}
