namespace BibleFTS.Api.Models
{
    public class Verse
    {
        public string Id { get;set; } = Guid.NewGuid().ToString();
        public string Book { get;set; } = String.Empty;
        public int Chapter { get;set; }
        public int VerseNumber { get;set; }
        public string Text { get;set; } = String.Empty;
    }
}