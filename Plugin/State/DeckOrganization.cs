using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MTGAEnhancementSuite.State
{
    /// <summary>
    /// A user-defined folder grouping a set of decks. Folders live entirely
    /// in the mod's settings file — never in MTGA's deck state, never in deck
    /// names, never on the wire. Opening the game on another machine without
    /// the mod produces the normal MTGA deck list.
    /// </summary>
    internal class DeckFolder
    {
        [JsonProperty("id")]
        public Guid Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Decks in this folder, ordered.</summary>
        [JsonProperty("deckIds")]
        public List<Guid> DeckIds { get; set; } = new List<Guid>();

        /// <summary>Unix-ms timestamp of folder creation. Used as a stable
        /// tiebreaker when two folders sort to the same position.</summary>
        [JsonProperty("createdAt")]
        public long CreatedAt { get; set; }

        public DeckFolder() { }

        public DeckFolder(string name)
        {
            Id = Guid.NewGuid();
            Name = name ?? "Untitled";
            DeckIds = new List<Guid>();
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    /// <summary>
    /// User-defined organization layered on top of MTGA's deck list. Stored
    /// per-machine in the mod settings; decks not appearing in any folder or
    /// in <see cref="RootOrder"/> fall back to MTGA's default ordering.
    /// </summary>
    internal class DeckOrganization
    {
        /// <summary>Ordered list of folders, displayed at the top of the deck grid.</summary>
        [JsonProperty("folders")]
        public List<DeckFolder> Folders { get; set; } = new List<DeckFolder>();

        /// <summary>
        /// User-overridden ordering for un-foldered decks at the root level.
        /// Decks not appearing here fall through to MTGA's order, appended
        /// after this list when rendered.
        /// </summary>
        [JsonProperty("rootOrder")]
        public List<Guid> RootOrder { get; set; } = new List<Guid>();
    }
}
