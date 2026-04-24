using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Morpheus.Sessions;

public sealed class SessionPage
{
    [JsonPropertyName("uuid")]   public string? Uuid { get; set; }
    [JsonPropertyName("at")]     public DateTime At { get; set; }
    [JsonPropertyName("text")]   public string Text { get; set; } = "";
}

public sealed class SessionLog
{
    [JsonPropertyName("id")]        public string Id { get; set; } = "";
    [JsonPropertyName("startedAt")] public DateTime StartedAt { get; set; }
    [JsonPropertyName("pages")]     public List<SessionPage> Pages { get; set; } = new();

    [JsonIgnore] public string? Path { get; set; }

    // Returns true when a NEW page was appended; false when we extended the last page in place.
    // Same UUID = same Claude turn = same page (text grows during streaming).
    public bool AppendOrUpdate(string? uuid, string text)
    {
        if (Pages.Count > 0 && string.Equals(Pages[^1].Uuid, uuid, StringComparison.Ordinal))
        {
            Pages[^1].Text = text;
            Pages[^1].At = DateTime.UtcNow;
            return false;
        }
        Pages.Add(new SessionPage { Uuid = uuid, Text = text, At = DateTime.UtcNow });
        return true;
    }
}
