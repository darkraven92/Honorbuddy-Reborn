using System.Globalization;

namespace Honorbuddy5875.Runtime;

internal sealed record GossipSelectionPlan(ulong NpcGuid, uint QuestId, int ActiveIndex, DialogQuest[] Entries)
{
    internal int LuaIndex => ActiveIndex + 1;
    internal string Command => GossipCommandInput.CommandFor(ActiveIndex);
    internal static GossipSelectionPlan Create(QuestDialogSnapshot dialog, ulong npcGuid, uint questId)
    {
        if (npcGuid == 0 || questId == 0 || dialog.GossipNpcGuid != npcGuid || dialog.NpcGuid != 0)
            throw new InvalidOperationException("The expected NPC's gossip list must be open, with no quest conversation active.");
        if (dialog.GossipQuests.Length > 32 || dialog.GossipQuests.Any(q => q.Id == 0))
            throw new InvalidOperationException("Invalid gossip quest list.");
        if (dialog.GossipQuests.Count(q => q.Id == questId) != 1)
            throw new InvalidOperationException("Expected quest is missing or duplicated in the gossip list.");
        var active = dialog.GossipQuests.Where(q => q.Status is 3 or 4).ToArray();
        int index = Array.FindIndex(active, q => q.Id == questId);
        if (index < 0) throw new InvalidOperationException("Expected quest is not in the active gossip list.");
        return new(npcGuid, questId, index, dialog.GossipQuests.ToArray());
    }
    internal void ValidateUnchanged(QuestDialogSnapshot dialog)
    {
        var current = Create(dialog, NpcGuid, QuestId);
        if (current.ActiveIndex != ActiveIndex || !Entries.SequenceEqual(current.Entries))
            throw new InvalidOperationException("Gossip list changed after planning; command was not submitted.");
    }
    internal bool HasSelectedQuest(QuestDialogSnapshot dialog) =>
        dialog.NpcGuid == NpcGuid && dialog.QuestId == QuestId && dialog.RawStage is 2 or 3 &&
        (dialog.GossipNpcGuid == 0 || dialog.GossipNpcGuid == NpcGuid);
}

internal enum GossipKeyboardLayout { Swedish, Us }
internal readonly record struct GossipKeyStroke(ushort Code, bool Shift);

// Restricted transport for one built-in game command. No generic Lua bridge, clipboard or chatlog.
internal static class GossipCommandInput
{
    internal static GossipKeyboardLayout ParseLayout(string? value) => value?.ToLowerInvariant() switch
    {
        "se" => GossipKeyboardLayout.Swedish,
        "us" => GossipKeyboardLayout.Us,
        _ => throw new ArgumentException("Execute requires --keyboard-layout se or --keyboard-layout us (the active layout inside WoW).")
    };
    internal static string CommandFor(int zeroBasedIndex)
    {
        if (zeroBasedIndex is < 0 or >= 32) throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex));
        return "/script SelectGossipActiveQuest(" + (zeroBasedIndex + 1).ToString(CultureInfo.InvariantCulture) + ")";
    }
    internal static GossipKeyStroke[] Encode(int index, GossipKeyboardLayout layout) =>
        CommandFor(index).Select(c => Key(c, layout)).ToArray();
    // Native CompleteQuest requests the reward offer. Original HB QuestFrame.CompleteQuest
    // additionally clicks reward buttons, so expose this action through HB ClickContinue only.
    internal const string ContinueCommand = "/script if IsQuestCompletable() then CompleteQuest() end";
    internal static GossipKeyStroke[] EncodeContinue(GossipKeyboardLayout layout) =>
        ContinueCommand.Select(c => Key(c, layout)).ToArray();
    internal const string AcceptCommand = "/script if GetNumQuestChoices()==0 then GetQuestReward() end";
    internal static GossipKeyStroke[] EncodeAccept(GossipKeyboardLayout layout) =>
        AcceptCommand.Select(c => Key(c, layout)).ToArray();
    internal static void SubmitAccept(GossipKeyboardLayout layout, Action validateBeforeSubmit,
        Action<ushort> tap, Action<bool> shift, Action<int> delay, CancellationToken cancellation) =>
        SubmitKeys(EncodeAccept(layout), validateBeforeSubmit, tap, shift, delay, cancellation);
    internal static ushort[] KeyCodes => new ushort[] { 1, 28, 42 }
        .Concat(Enum.GetValues<GossipKeyboardLayout>().SelectMany(layout =>
            Enumerable.Range(0, 32).SelectMany(i => Encode(i, layout)).Select(k => k.Code)))
        .Concat(Enum.GetValues<GossipKeyboardLayout>().SelectMany(layout => EncodeContinue(layout)).Select(k => k.Code))
        .Concat(Enum.GetValues<GossipKeyboardLayout>().SelectMany(layout => EncodeAccept(layout)).Select(k => k.Code))
        .Distinct().ToArray();

    private static GossipKeyStroke Key(char c, GossipKeyboardLayout layout)
    {
        if (layout is not (GossipKeyboardLayout.Swedish or GossipKeyboardLayout.Us))
            throw new ArgumentOutOfRangeException(nameof(layout));
        bool shift = char.IsAsciiLetterUpper(c);
        char lower = char.ToLowerInvariant(c);
        const string top = "qwertyuiop", middle = "asdfghjkl", bottom = "zxcvbnm";
        int i;
        if ((i = top.IndexOf(lower)) >= 0) return new((ushort)(16 + i), shift);
        if ((i = middle.IndexOf(lower)) >= 0) return new((ushort)(30 + i), shift);
        if ((i = bottom.IndexOf(lower)) >= 0) return new((ushort)(44 + i), shift);
        if (c is >= '1' and <= '9') return new((ushort)(2 + c - '1'), false);
        if (c == '0') return new(11, false);
        return c switch
        {
            '=' => layout == GossipKeyboardLayout.Swedish ? new(11, true) : new(13, false),
            ' ' => new(57, false),
            '/' => layout == GossipKeyboardLayout.Swedish ? new(8, true) : new(53, false),
            '(' => layout == GossipKeyboardLayout.Swedish ? new(9, true) : new(10, true),
            ')' => layout == GossipKeyboardLayout.Swedish ? new(10, true) : new(11, true),
            _ => throw new InvalidOperationException("Unsupported command character.")
        };
    }

    // Callbacks permit offline validation of the real event sequencing and cancellation path.
    internal static void Submit(int index, GossipKeyboardLayout layout, Action validateBeforeSubmit,
        Action<ushort> tap, Action<bool> shift, Action<int> delay, CancellationToken cancellation)
    {
        var keys = Encode(index, layout); // Validate/encode before emitting any input.
        SubmitKeys(keys, validateBeforeSubmit, tap, shift, delay, cancellation);
    }

    internal static void SubmitContinue(GossipKeyboardLayout layout, Action validateBeforeSubmit,
        Action<ushort> tap, Action<bool> shift, Action<int> delay, CancellationToken cancellation) =>
        SubmitKeys(EncodeContinue(layout), validateBeforeSubmit, tap, shift, delay, cancellation);

    private static void SubmitKeys(GossipKeyStroke[] keys, Action validateBeforeSubmit,
        Action<ushort> tap, Action<bool> shift, Action<int> delay, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        validateBeforeSubmit();
        bool opened = false, submitted = false;
        try
        {
            opened = true;
            tap(28); // Enter opens the default chat edit box. User must begin with chat closed.
            delay(150);
            foreach (var key in keys)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (key.Shift) shift(true);
                    tap(key.Code);
                }
                finally { if (key.Shift) shift(false); }
                delay(25);
            }
            cancellation.ThrowIfCancellationRequested();
            validateBeforeSubmit(); // Re-read quest, NPC, range, health and complete gossip list.
            cancellation.ThrowIfCancellationRequested();
            tap(28); // One submission; never retry automatically.
            submitted = true;
        }
        finally
        {
            try { shift(false); }
            finally
            {
                if (opened && !submitted)
                    tap(1); // Cancel the partially typed command; do not submit it.
            }
        }
    }
}
