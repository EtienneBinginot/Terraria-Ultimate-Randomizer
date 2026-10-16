using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria.UI;
using Terraria.UI.Chat;

namespace UltimateRandomizer.Common.Journal
{
	// A text line that draws item icons ([i:id] tags) and shows their tooltip on hover
	public class JournalRow : UIElement
	{
		private readonly TextSnippet[] snippets;

		public JournalRow(string text)
		{
			snippets = ChatManager.ParseMessage(text, Color.White).ToArray();
			ChatManager.ConvertNormalSnippets(snippets);
			int lines = text.Count(c => c == '\n') + 1;
			Width.Set(0, 1f);
			Height.Set(lines * 30 + 4, 0);
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();
			ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.MouseText.Value, snippets, new Vector2(dims.X + 4, dims.Y + 4),
				0f, Vector2.Zero, Vector2.One, out int hovered, dims.Width - 8, 2f);
			if (hovered >= 0 && IsMouseHovering) snippets[hovered].OnHover();
		}
	}
}
