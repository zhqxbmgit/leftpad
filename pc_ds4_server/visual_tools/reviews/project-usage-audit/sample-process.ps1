param(
    [Parameter(Mandatory = $true)]
    [int]$RootPid,
    [Parameter(Mandatory = $true)]
    [int]$Round
)

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class LeftPadAuditGuiResources
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr process, int flags);

    public static Dictionary<int, int> ParentMap()
    {
        var result = new Dictionary<int, int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == new IntPtr(-1)) return result;
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snapshot, ref entry)) return result;
            do { result[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID; }
            while (Process32Next(snapshot, ref entry));
            return result;
        }
        finally { CloseHandle(snapshot); }
    }

    public static int GuiResources(int processId, int flags)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero) return -1;
        try { return GetGuiResources(handle, flags); }
        finally { CloseHandle(handle); }
    }
}
'@

$parentMap = [LeftPadAuditGuiResources]::ParentMap()
$descendantIds = [System.Collections.Generic.HashSet[int]]::new()
[void]$descendantIds.Add($RootPid)
do {
    $added = $false
    foreach ($entry in $parentMap.GetEnumerator()) {
        if ($descendantIds.Contains([int]$entry.Value) -and
            $descendantIds.Add([int]$entry.Key)) {
            $added = $true
        }
    }
} while ($added)

$root = Get-Process -Id $RootPid -ErrorAction Stop
$webProcesses = @(Get-Process -Name msedgewebview2 -ErrorAction SilentlyContinue | Where-Object {
    $descendantIds.Contains([int]$_.Id)
})

[pscustomobject]@{
    round = $Round
    timestampUtc = [DateTime]::UtcNow.ToString('O')
    rootPid = $RootPid
    host = [pscustomobject]@{
        privateBytes = [long]$root.PrivateMemorySize64
        workingSetBytes = [long]$root.WorkingSet64
        handleCount = [int]$root.HandleCount
        gdiHandles = [LeftPadAuditGuiResources]::GuiResources($RootPid, 0)
        userHandles = [LeftPadAuditGuiResources]::GuiResources($RootPid, 1)
        threadCount = [int]$root.Threads.Count
    }
    processTreeCount = $descendantIds.Count
    webView2 = [pscustomobject]@{
        processCount = $webProcesses.Count
        privateBytes = [long](($webProcesses | Measure-Object PrivateMemorySize64 -Sum).Sum)
        workingSetBytes = [long](($webProcesses | Measure-Object WorkingSet64 -Sum).Sum)
        handleCount = [int](($webProcesses | Measure-Object HandleCount -Sum).Sum)
        threadCount = [int](($webProcesses | ForEach-Object { $_.Threads.Count } | Measure-Object -Sum).Sum)
        pids = @($webProcesses | Select-Object -ExpandProperty Id | Sort-Object)
    }
} | ConvertTo-Json -Depth 5 -Compress
