using System.Globalization;

namespace TF2IdleManager;

// Read-only weekly/session presentation over the established account-local stores.
// Neither the drop monitor nor the session/watchdog pipeline is modified here.
public sealed partial class MainForm
{
    private sealed record WeeklySelection(DateTimeOffset Start, bool IsCurrent)
    {
        public override string ToString() =>
            $"{Start:dd MMM yyyy} – {Start.AddDays(7):dd MMM yyyy}" + (IsCurrent ? " · current" : "");
    }

    private sealed record WeekViewRow(
        DateTimeOffset Start, int Regular, int Accepted, int Containers,
        int Pending, TimeSpan Eligible, TimeSpan Managed);

    private static IReadOnlyList<WeekViewRow> BuildWeekViews(
        IReadOnlyList<DropHistoryEntry> entries, IReadOnlyList<WeeklyUsageBucket> usage,
        DateTimeOffset currentWeek)
    {
        var buckets = usage.GroupBy(x => x.WindowStartUtc)
            .ToDictionary(x => x.Key, x => x.Last());
        var groups = entries.GroupBy(x => DropHistoryStore.GetTf2WeekStartUtc(x.DetectedAt))
            .ToDictionary(x => x.Key, x => x.ToList());
        var starts = new SortedSet<DateTimeOffset>(groups.Keys);
        starts.UnionWith(buckets.Keys);
        starts.Add(currentWeek);
        return starts.Reverse().Take(18).Select(start =>
        {
            var items = groups.GetValueOrDefault(start) ?? new List<DropHistoryEntry>();
            var regular = items.Where(x => x.CountsTowardWeeklyMetrics).ToList();
            buckets.TryGetValue(start, out var bucket);
            return new WeekViewRow(start, regular.Count,
                regular.Count(x => x.AcknowledgedAt.HasValue),
                items.Count - regular.Count, items.Count(x => x.AcknowledgedAt is null),
                TimeSpan.FromSeconds(Math.Max(0, bucket?.EligibleSeconds ?? 0)),
                TimeSpan.FromSeconds(Math.Max(0, bucket?.ManagedSeconds ?? 0)));
        }).ToList();
    }

    private void RefreshWeeklyExperience()
    {
        _nextWeeklyRefresh = DateTimeOffset.UtcNow.AddSeconds(30);
        if (!string.Equals(_weeklyViewSteamId, AccountDataContext.ActiveSteamId64, StringComparison.Ordinal))
        {
            _weeklyViewSteamId = AccountDataContext.ActiveSteamId64;
            _weeklySelectedStart = null;
        }
        if (!AccountDataContext.HasActiveAccount)
        {
            _weeklyTitle.Text = "No active Steam account · use Accounts to detect one";
            _weeklyRegularMetric.Text = "—";
            _weeklyContainerMetric.Text = "—";
            _weeklyTimeMetric.Text = "—";
            _weeklyComparison.Text = "—";
            _weeklyTrendGrid.DataSource = null;
            ReplaceWeeklyItemCards(Array.Empty<DropHistoryEntry>());
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var currentStart = DropHistoryStore.GetTf2WeekStartUtc(now);
        var all = DropHistoryStore.GetAllEntries();
        var usage = WeeklyUsageStore.GetRecordedWeeks();
        var rows = BuildWeekViews(all, usage, currentStart);
        if (_weeklySelectedStart is null || rows.All(x => x.Start != _weeklySelectedStart))
            _weeklySelectedStart = currentStart;
        _weeklyChangingSelection = true;
        try
        {
            _weeklyWeekSelector.BeginUpdate();
            _weeklyWeekSelector.Items.Clear();
            foreach (var row in rows)
                _weeklyWeekSelector.Items.Add(new WeeklySelection(row.Start, row.Start == currentStart));
            var idx = rows.ToList().FindIndex(x => x.Start == _weeklySelectedStart.Value);
            _weeklyWeekSelector.SelectedIndex = idx < 0 ? 0 : idx;
            _weeklyWeekSelector.EndUpdate();
        }
        finally { _weeklyChangingSelection = false; }

        var chosen = rows.First(x => x.Start == _weeklySelectedStart);
        _weeklyTitle.Text = (chosen.Start == currentStart ? "Current TF2 week" : "Past TF2 week") +
            $" · {chosen.Start:dd MMM yyyy} to {chosen.Start.AddDays(7):dd MMM yyyy} (UTC)";
        _weeklyRegularMetric.Text = $"{chosen.Regular}  /  {chosen.Accepted} accepted";
        _weeklyContainerMetric.Text = chosen.Containers.ToString(CultureInfo.InvariantCulture);
        _weeklyTimeMetric.Text = FormatDuration(chosen.Eligible);
        var previous = rows.FirstOrDefault(x => x.Start == chosen.Start.AddDays(-7));
        _weeklyComparison.Text = previous is null
            ? "No prior data"
            : $"{(chosen.Regular - previous.Regular >= 0 ? "+" : "")}{chosen.Regular - previous.Regular} drops\n" +
              $"{(int)Math.Round((chosen.Eligible - previous.Eligible).TotalMinutes)}m tracked";
        _weeklyComparison.Tag = "muted";

        // The comparison is not a Valve weekly-pool counter; it uses only observations
        // from this SteamID's on-disk history, with no inferred session hours.
        _weeklyTrendGrid.DataSource = rows.OrderByDescending(x => x.Start).Select(x => new
        {
            Week = x.Start.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            Regular = x.Regular,
            Accepted = x.Accepted,
            Containers = x.Containers,
            Tracked = FormatDuration(x.Eligible),
            Pending = x.Pending
        }).ToList();
        foreach (DataGridViewColumn column in _weeklyTrendGrid.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        for (int i = 0; i < _weeklyTrendGrid.Rows.Count && i < rows.Count; i++)
            _weeklyTrendGrid.Rows[i].Tag = rows[i].Start;
        DashboardStyleGrid(_weeklyTrendGrid, _settings.DarkMode);

        var itemsOfWeek = all.Where(x => DropHistoryStore.GetTf2WeekStartUtc(x.DetectedAt) == chosen.Start)
            .OrderByDescending(x => x.DetectedAt).ToList();
        ReplaceWeeklyItemCards(itemsOfWeek);
    }

    private void ExportSelectedWeekCsv()
    {
        if (!AccountDataContext.HasActiveAccount || _weeklySelectedStart is null)
        {
            MessageBox.Show(this, "Select a Steam account and week first.");
            return;
        }
        var selectedWeek = _weeklySelectedStart.Value;
        using var dialog = new SaveFileDialog
        {
            Title = "Export selected TF2 week",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"tf2-week-{AccountDataContext.ActiveSteamId64}-{selectedWeek:yyyyMMdd}.csv",
            AddExtension = true, DefaultExt = "csv", OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var items = DropHistoryStore.GetAllEntries()
                .Where(x => DropHistoryStore.GetTf2WeekStartUtc(x.DetectedAt) == selectedWeek);
            CsvExportService.ExportDrops(dialog.FileName, items);
            AppendLog($"CSV export complete: selected TF2 week {selectedWeek:yyyy-MM-dd} | SteamID={AccountDataContext.ActiveSteamId64}");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "CSV export failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ReplaceWeeklyItemCards(IReadOnlyList<DropHistoryEntry> entries)
    {
        _historyArtworkResolver.ReloadFromDiskIfChanged();
        _weeklyItemCards.SuspendLayout();
        var old = _weeklyItemCards.Controls.Cast<Control>().ToArray();
        _weeklyItemCards.Controls.Clear();
        foreach (var control in old) control.Dispose();
        var width = Math.Max(320, _weeklyItemCards.ClientSize.Width - 28);
        foreach (var entry in entries.Take(100))
        {
            var row = new Panel { Width = width, Height = 79, Margin = new Padding(0, 0, 0, 7), Tag = "field" };
            var picture = new PictureBox
            {
                Location = new Point(9, 8), Size = new Size(66, 62), SizeMode = PictureBoxSizeMode.Zoom
            };
            var fallback = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Text = entry.CountsTowardWeeklyMetrics ? "✦" : "▣", Font = new Font("Segoe UI", 24), Tag = "warning" };
            picture.Controls.Add(fallback);
            row.Controls.Add(picture);
            var name = UiLabel(entry.DisplayName, 10.1f, true);
            name.Location = new Point(87, 5); name.Size = new Size(Math.Max(190, width - 96), 25); name.Dock = DockStyle.None;
            row.Controls.Add(name);
            var status = entry.AcknowledgedAt is null ? "Pending" : "Accepted";
            var detail = UiLabel($"{(entry.CountsTowardWeeklyMetrics ? "REGULAR" : "CONTAINER")} · {entry.DetectedAt.ToLocalTime():dd MMM HH:mm} · {status}" +
                (entry.Recovered ? " · recovered (original time unknown)" : ""), 8.5f, false,
                entry.AcknowledgedAt.HasValue ? "good" : "warning");
            detail.Location = new Point(87, 33); detail.Size = new Size(Math.Max(190, width - 96), 28); detail.Dock = DockStyle.None;
            row.Controls.Add(detail);
            _weeklyItemCards.Controls.Add(row);
            var url = _historyArtworkResolver.GetCachedImageUrl(entry.DefIndex);
            if (!string.IsNullOrEmpty(url)) _ = DashboardItemImages.FillAsync(picture, fallback, url);
        }
        if (entries.Count == 0)
        {
            var empty = UiLabel("No observed items this week. A container does not count as a regular drop.", 10, false, "muted");
            empty.Size = new Size(Math.Max(320, width), 72);
            _weeklyItemCards.Controls.Add(empty);
        }
        _weeklyItemCards.ResumeLayout();
        ApplyDashboardTheme();
    }

    private void RefreshHistoricalSessionCards(IReadOnlyList<DropHistoryEntry> entries)
    {
        _historyVisual.SuspendLayout();
        var old = _historyVisual.Controls.Cast<Control>().ToArray();
        _historyVisual.Controls.Clear();
        foreach (var control in old) control.Dispose();
        var width = Math.Max(330, _historyVisual.ClientSize.Width - 27);

        var ledger = SessionHistoryStore.ReadAll();
        var knownIds = ledger.Select(x => x.SessionId).ToHashSet(StringComparer.Ordinal);
        var grouped = entries.Where(x => !string.IsNullOrWhiteSpace(x.SessionId))
            .GroupBy(x => x.SessionId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList());

        void AddRow(string titleText, string summaryText, string noteText)
        {
            var row = new Panel { Width = width, Height = 104, Tag = "field", Margin = new Padding(0, 0, 0, 9) };
            var title = UiLabel(titleText, 10.2f, true);
            title.Dock = DockStyle.None; title.Location = new Point(14, 7); title.Size = new Size(width - 30, 28);
            var details = UiLabel(summaryText, 9, false, "good");
            details.Dock = DockStyle.None; details.Location = new Point(14, 34); details.Size = new Size(width - 30, 25);
            var note = UiLabel(noteText, 8.6f, false, "muted");
            note.Dock = DockStyle.None; note.Location = new Point(14, 63); note.Size = new Size(width - 30, 30);
            row.Controls.Add(title); row.Controls.Add(details); row.Controls.Add(note);
            _historyVisual.Controls.Add(row);
        }

        foreach (var session in ledger.Take(100))
        {
            var items = grouped.GetValueOrDefault(session.SessionId) ?? new List<DropHistoryEntry>();
            var duration = (session.EndedAt ?? session.LastUpdatedAt) - session.StartedAt;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
            AddRow($"{session.StartedAt.ToLocalTime():dd MMM yyyy HH:mm} · {session.State} · {session.SelectedClass}",
                $"Duration {FormatDuration(duration)} · eligible {FormatDuration(TimeSpan.FromSeconds(Math.Max(0, session.EligibleSeconds)))} · " +
                $"API outage {FormatDuration(TimeSpan.FromSeconds(Math.Max(0, session.ApiOutageSeconds)))}",
                $"{items.Count(x => x.CountsTowardWeeklyMetrics)} regular · {items.Count(x => !x.CountsTowardWeeklyMetrics)} containers · " +
                $"{items.Count(x => x.AcknowledgedAt.HasValue)} accepted · server: {session.Server}");
        }

        // Backward compatibility: older session IDs have item evidence but no exact start/end record.
        var legacySessions = grouped.Where(g => !knownIds.Contains(g.Key))
            .OrderByDescending(g => g.Value.Max(x => x.DetectedAt)).Take(80).ToList();
        foreach (var group in legacySessions)
        {
            var items = group.Value.OrderBy(x => x.DetectedAt).ToList();
            var first = items.First(); var last = items.Last();
            AddRow($"Historical session {group.Key} · {last.DetectedAt.ToLocalTime():dd MMM yyyy}",
                $"{items.Count(x => x.CountsTowardWeeklyMetrics)} regular · {items.Count(x => !x.CountsTowardWeeklyMetrics)} containers · " +
                $"{items.Count(x => x.AcknowledgedAt.HasValue)} accepted",
                $"Item observations: {first.DetectedAt.ToLocalTime():HH:mm}–{last.DetectedAt.ToLocalTime():HH:mm} · " +
                "full session duration unavailable (legacy record)");
        }

        if (ledger.Count == 0 && legacySessions.Count == 0)
        {
            var empty = UiLabel("No sessions recorded yet. New managed sessions are recorded even without drops.", 10, false, "muted");
            empty.Size = new Size(width, 80);
            _historyVisual.Controls.Add(empty);
        }
        _historyVisual.ResumeLayout();
        ApplyDashboardTheme();
    }

}
