using System.Text.Json;

namespace Gwendoline;

/// <summary>
/// Keeps the PC awake with the lid closed: the lid action of every power plan becomes Do nothing.
/// The original actions are written to state.json before anything changes, so they always come back,
/// after a crash or a restart included. The setting survives a restart, the file too.
/// Changing the lid action needs no administrator rights.
/// </summary>
internal static class LidSleep
{
    private static Guid buttons = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    private static Guid lidAction = new("5ca83367-6e45-459f-a27b-476b1d01c936");
    private const uint DoNothing = 0;
    private const uint ErrorFileNotFound = 2;

    /// <summary>What Gwendoline changed: the lid actions it replaced, by power plan.</summary>
    private sealed record Saved(bool On, Dictionary<Guid, uint[]> Originals);

    private static string File => Path.Combine(Paths.Data, "state.json");

    /// <summary>Gwendoline is on: the lid actions it replaced are on disk, waiting to come back.</summary>
    public static bool IsOn => Load().On;

    /// <summary>Saves the lid action of every power plan, then sets them all to Do nothing. Returns false if Windows refused.</summary>
    public static bool Enable()
    {
        var saved = Load();
        var originals = new Dictionary<Guid, uint[]>(saved.Originals);
        foreach (var scheme in Schemes())
        {
            if (!originals.ContainsKey(scheme) && Read(scheme) is uint[] values)
            {
                originals[scheme] = values;
            }
        }
        if (originals.Count == 0 || !Save(new Saved(true, originals)))
        {
            return false;
        }

        var ok = originals.Keys.All(scheme => Write(scheme, [DoNothing, DoNothing]));
        ok &= ApplyActive();
        if (ok && ActiveScheme() is Guid active && Read(active) is [DoNothing, DoNothing])
        {
            return true;
        }
        Restore();
        return false;
    }

    /// <summary>
    /// Gives every power plan its lid action back, except <paramref name="keep"/>, changed by the user while Gwendoline was on.
    /// Returns false if Windows refused: state.json then stays, to try again.
    /// </summary>
    public static bool Restore(Guid? keep = null)
    {
        var saved = Load();
        if (!saved.On && saved.Originals.Count == 0)
        {
            return true;
        }
        var ok = true;
        foreach (var (scheme, values) in saved.Originals)
        {
            if (scheme != keep)
            {
                ok &= Write(scheme, values);
            }
        }
        ok &= ApplyActive();
        if (ok)
        {
            Delete();
        }
        return ok;
    }

    /// <summary>
    /// While on, keeps the active power plan in line. A plan Gwendoline has not seen yet (switched in
    /// Windows or by the laptop's own app) gets Do nothing too. A plan it already changed whose lid
    /// action is back to something else was changed in Windows settings: returns it, Gwendoline turns off.
    /// </summary>
    public static Guid? Reconcile()
    {
        var saved = Load();
        if (!saved.On || ActiveScheme() is not Guid active || Read(active) is not uint[] values)
        {
            return null;
        }
        if (!saved.Originals.ContainsKey(active))
        {
            var originals = new Dictionary<Guid, uint[]>(saved.Originals) { [active] = values };
            if (Save(saved with { Originals = originals }))
            {
                Write(active, [DoNothing, DoNothing]);
                ApplyActive();
            }
            return null;
        }
        return values is [DoNothing, DoNothing] ? null : active;
    }

    private static Saved Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Saved>(System.IO.File.ReadAllText(File)) ?? new Saved(false, []);
        }
        catch (FileNotFoundException)
        {
            return new Saved(false, []);
        }
        catch (DirectoryNotFoundException)
        {
            return new Saved(false, []);
        }
        catch (JsonException)
        {
            // A torn write: nothing reliable to restore, Windows keeps what it has.
            return new Saved(false, []);
        }
    }

    private static bool Save(Saved saved)
    {
        try
        {
            Directory.CreateDirectory(Paths.Data);
            var temporary = File + ".tmp";
            System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(saved));
            System.IO.File.Move(temporary, File, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Delete()
    {
        try
        {
            System.IO.File.Delete(File);
        }
        catch (IOException)
        {
            Save(new Saved(false, []));
        }
    }

    private static IEnumerable<Guid> Schemes()
    {
        for (uint index = 0; ; index++)
        {
            var scheme = Guid.Empty;
            var size = (uint)16;
            if (Native.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, Native.AccessScheme, index, ref scheme, ref size) != 0)
            {
                yield break;
            }
            yield return scheme;
        }
    }

    private static Guid? ActiveScheme()
    {
        if (Native.PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0)
        {
            return null;
        }
        try
        {
            return System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            Native.LocalFree(pointer);
        }
    }

    /// <summary>The lid action on the adapter, then on battery.</summary>
    private static uint[]? Read(Guid scheme)
    {
        if (Native.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref buttons, ref lidAction, out var ac) != 0
            || Native.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref buttons, ref lidAction, out var dc) != 0)
        {
            return null;
        }
        return [ac, dc];
    }

    private static bool Write(Guid scheme, uint[] values)
    {
        var ac = Native.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref buttons, ref lidAction, values[0]);
        var dc = Native.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref buttons, ref lidAction, values[1]);
        // A plan deleted since it was saved has nothing left to restore.
        return ac is 0 or ErrorFileNotFound && dc is 0 or ErrorFileNotFound;
    }

    /// <summary>Windows reads a change to the active plan only once the plan is applied again.</summary>
    private static bool ApplyActive()
    {
        if (ActiveScheme() is not Guid active)
        {
            return false;
        }
        return Native.PowerSetActiveScheme(IntPtr.Zero, ref active) == 0;
    }
}
