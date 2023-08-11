using System;
using System.Runtime.InteropServices;

public class SystemSoundPlayer
{
    // Import the WinAPI PlaySound function
    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(string sound, IntPtr hMod, uint flags);

    public static void PlaySystemSound(SystemSoundType systemSoundType)
    {
        string soundAlias = GetSystemSoundAlias(systemSoundType);
        PlaySound(soundAlias, IntPtr.Zero, 0x0001 | 0x0002);
    }

    private static string GetSystemSoundAlias(SystemSoundType systemSoundType)
    {
        return systemSoundType switch
        {
            SystemSoundType.Beep => "Default",
            SystemSoundType.Exclamation => "SystemExclamation",
            SystemSoundType.Hand => "SystemHand",
            SystemSoundType.Question => "SystemQuestion",
            _ => "Default"
        };
    }

    public static void ListAllSystemSounds()
    {
        foreach (var systemSoundType in Enum.GetValues(typeof(SystemSoundType)))
        {
            Console.WriteLine($"{systemSoundType}: {Enum.GetName(typeof(SystemSoundType), systemSoundType)}");
        }
    }
}

public enum SystemSoundType
{
    Beep,
    Exclamation,
    Hand,
    Question
}