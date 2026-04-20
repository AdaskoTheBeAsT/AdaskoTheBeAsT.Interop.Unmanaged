using System;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

/// <summary>
/// Specifies flags passed to the Windows <c>LoadLibraryEx</c> function.
/// </summary>
/// <remarks>
/// <para>
/// Values mirror the <c>dwFlags</c> parameter documented at
/// <see href="https://learn.microsoft.com/windows/win32/api/libloaderapi/nf-libloaderapi-loadlibraryexa"/>.
/// </para>
/// <para>
/// Use these values with <see cref="UnmanagedLibrary"/> or
/// <see cref="UnmanagedLibrary.LoadLibrary(string, LoadLibraryFlags)"/> to control how a DLL is
/// located and initialized. The default APIs in this package use
/// <see cref="LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR"/> together with
/// <see cref="LOAD_LIBRARY_SEARCH_SYSTEM32"/>.
/// </para>
/// </remarks>
[Flags]
#pragma warning disable S2344, S2346
public enum LoadLibraryFlags : uint
{
    /// <summary>
    /// No flags. Passing this value makes <c>LoadLibraryEx</c> behave identically to
    /// <c>LoadLibrary</c>, using the standard DLL search path with no optional behaviors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Extension: provided so callers can explicitly document "no flags" at the call site
    /// instead of passing a bare literal <c>0</c>. When this value is used the
    /// <c>LoadLibraryEx</c> function resolves dependencies, executes <c>DllMain</c>, and
    /// maps the module as an executable DLL.
    /// </para>
    /// </remarks>
    NONE = 0x00000000,

    /// <summary>
    /// If this value is used, and the executable module is a DLL, the system does not call
    /// <c>DllMain</c> for process and thread initialization and termination. Also, the
    /// system does not load additional executable modules that are referenced by the
    /// specified module.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Note: Do not use this value; it is provided only for backward compatibility. If you
    /// are planning to access only data or resources in the DLL, use
    /// <see cref="LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE"/> or
    /// <see cref="LOAD_LIBRARY_AS_IMAGE_RESOURCE"/> or both. Otherwise, load the library as
    /// a DLL or executable module using the <c>LoadLibrary</c> function.
    /// </para>
    /// <para>
    /// Extension: because dependencies are not resolved, any export obtained through this
    /// handle that transitively calls into another module will fail at runtime. Prefer the
    /// resource/data-file flags above for safe read-only access.
    /// </para>
    /// </remarks>
    DONT_RESOLVE_DLL_REFERENCES = 0x00000001,

    /// <summary>
    /// If this value is used, the system maps the file into the calling process's virtual
    /// address space as if it were a data file. Nothing is done to execute or prepare to
    /// execute the mapped file. Therefore, you cannot call functions like
    /// <c>GetModuleFileName</c>, <c>GetModuleHandle</c> or <c>GetProcAddress</c> with this
    /// DLL. Using this value causes writes to read-only memory to raise an access
    /// violation. Use this flag when you want to load a DLL only to extract messages or
    /// resources from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value can be used with <see cref="LOAD_LIBRARY_AS_IMAGE_RESOURCE"/>.
    /// </para>
    /// <para>
    /// Extension: this flag does not prevent other processes from modifying the module
    /// while it is loaded. For security reasons Microsoft recommends
    /// <see cref="LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE"/> in most scenarios. Do not combine
    /// <c>LOAD_LIBRARY_AS_DATAFILE</c> and <c>LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE</c> in the
    /// same call. A handle returned under this flag is flagged - test it with the
    /// <c>LDR_IS_DATAFILE</c> macro before calling any module API on it.
    /// </para>
    /// </remarks>
    LOAD_LIBRARY_AS_DATAFILE = 0x00000002,

    /// <summary>
    /// If this value is used and <c>lpFileName</c> specifies an absolute path, the system
    /// uses the alternate file search strategy discussed in the Remarks section of
    /// <c>LoadLibraryEx</c> to find associated executable modules that the specified module
    /// causes to be loaded. If this value is used and <c>lpFileName</c> specifies a
    /// relative path, the behavior is undefined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If this value is not used, or if <c>lpFileName</c> does not specify a path, the
    /// system uses the standard search strategy discussed in the Remarks section to find
    /// associated executable modules that the specified module causes to be loaded. This
    /// value cannot be combined with any <c>LOAD_LIBRARY_SEARCH</c> flag.
    /// </para>
    /// <para>
    /// Extension: the alternate strategy searches the directory of the specified module
    /// first for its dependencies, which can silently pick up a side-by-side DLL next to
    /// the target. On modern Windows the safer option is to opt in to a restricted search
    /// via <see cref="LOAD_LIBRARY_SEARCH_DEFAULT_DIRS"/> combined with
    /// <see cref="LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR"/>.
    /// </para>
    /// </remarks>
    LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008,

    /// <summary>
    /// If this value is used, the system does not check AppLocker rules or apply Software
    /// Restriction Policies for the DLL. This action applies only to the DLL being loaded
    /// and not to its dependencies. This value is recommended for use in setup programs
    /// that must run extracted DLLs during installation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows Server 2008 R2 and Windows 7: On systems with KB2532445 installed, the
    /// caller must be running as <c>LocalSystem</c> or <c>TrustedInstaller</c>; otherwise
    /// the system ignores this flag. For more information, see
    /// <see href="https://support.microsoft.com/kb/2532445"/>.
    /// </para>
    /// <para>
    /// Windows Server 2008, Windows Vista, Windows Server 2003 and Windows XP: AppLocker
    /// was introduced in Windows 7 and Windows Server 2008 R2.
    /// </para>
    /// <para>
    /// Extension: reserve this flag for trusted installers that run under elevated
    /// identity. In user-facing applications it bypasses a security boundary and should
    /// not be used to "fix" DLLs blocked by policy.
    /// </para>
    /// </remarks>
    LOAD_IGNORE_CODE_AUTHZ_LEVEL = 0x00000010,

    /// <summary>
    /// If this value is used, the system maps the file into the process's virtual address
    /// space as an image file. However, the loader does not load the static imports or
    /// perform the other usual initialization steps. Use this flag when you want to load a
    /// DLL only to extract messages or resources from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unless the application depends on the file having the in-memory layout of an image,
    /// this value should be used with either <see cref="LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE"/>
    /// or <see cref="LOAD_LIBRARY_AS_DATAFILE"/>.
    /// </para>
    /// <para>
    /// Windows Server 2003 and Windows XP: This value is not supported until Windows Vista.
    /// </para>
    /// <para>
    /// Extension: image-resource mapping preserves PE section alignment, so RVA-based
    /// resource lookups are faster than with <see cref="LOAD_LIBRARY_AS_DATAFILE"/>, and
    /// other processes cannot modify the file while it is mapped. Combining it with
    /// <see cref="LOAD_LIBRARY_AS_DATAFILE"/> / <see cref="LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE"/>
    /// lets the loader pick the most memory-efficient mapping automatically.
    /// </para>
    /// </remarks>
    LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020,

    /// <summary>
    /// Similar to <see cref="LOAD_LIBRARY_AS_DATAFILE"/>, except that the DLL file is
    /// opened with exclusive write access for the calling process. Other processes cannot
    /// open the DLL file for write access while it is in use. However, the DLL can still
    /// be opened by other processes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value can be used with <see cref="LOAD_LIBRARY_AS_IMAGE_RESOURCE"/>.
    /// </para>
    /// <para>
    /// Windows Server 2003 and Windows XP: This value is not supported until Windows Vista.
    /// </para>
    /// <para>
    /// Extension: this is the recommended way to read-map a module whose bytes must not
    /// change while you are reading resources from it - for example when computing a hash
    /// or signature. Do not combine with <see cref="LOAD_LIBRARY_AS_DATAFILE"/>.
    /// </para>
    /// </remarks>
    LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x00000040,

    /// <summary>
    /// Specifies that the digital signature of the binary image must be checked at load
    /// time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value requires Windows 8.1, Windows 10 or later.
    /// </para>
    /// <para>
    /// Extension: use this flag to harden load operations against tampered or unsigned
    /// binaries, for example when loading a third-party native dependency that is expected
    /// to be Authenticode-signed. The call fails with <c>ERROR_INVALID_IMAGE_HASH</c> (or a
    /// related signing error) when the signature cannot be verified, allowing the caller
    /// to refuse untrusted modules without executing them. Because the flag is a no-op on
    /// older Windows versions, pair it with a runtime OS-version check if your application
    /// still targets Windows 7 or Windows Server 2008 R2.
    /// </para>
    /// </remarks>
    LOAD_LIBRARY_REQUIRE_SIGNED_TARGET = 0x00000080,

    /// <summary>
    /// If this value is used, the directory that contains the DLL is temporarily added to
    /// the beginning of the list of directories that are searched for the DLL's
    /// dependencies. Directories in the standard search path are not searched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>lpFileName</c> parameter must specify a fully qualified path. This value
    /// cannot be combined with <see cref="LOAD_WITH_ALTERED_SEARCH_PATH"/>.
    /// </para>
    /// <para>
    /// For example, if <c>Lib2.dll</c> is a dependency of <c>C:\Dir1\Lib1.dll</c>, loading
    /// <c>Lib1.dll</c> with this value causes the system to search for <c>Lib2.dll</c>
    /// only in <c>C:\Dir1</c>. To search for <c>Lib2.dll</c> in <c>C:\Dir1</c> and all of
    /// the directories in the DLL search path, combine this value with
    /// <see cref="LOAD_LIBRARY_SEARCH_DEFAULT_DIRS"/>.
    /// </para>
    /// <para>
    /// Windows 7, Windows Server 2008 R2, Windows Vista and Windows Server 2008: This
    /// value requires <see href="https://support.microsoft.com/kb/2533623">KB2533623</see>
    /// to be installed.
    /// </para>
    /// <para>
    /// Windows Server 2003 and Windows XP: This value is not supported.
    /// </para>
    /// <para>
    /// Extension: this is the single most important flag for shipping plug-ins or
    /// redistributed native libraries, because it ensures dependencies are resolved from
    /// the plug-in's own folder rather than the caller's current directory or <c>PATH</c>.
    /// It is part of the default flag combination used by <see cref="UnmanagedLibrary"/>.
    /// </para>
    /// </remarks>
    LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100,

    /// <summary>
    /// If this value is used, the application's installation directory is searched for the
    /// DLL and its dependencies. Directories in the standard search path are not searched.
    /// This value cannot be combined with <see cref="LOAD_WITH_ALTERED_SEARCH_PATH"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows 7, Windows Server 2008 R2, Windows Vista and Windows Server 2008: This
    /// value requires <see href="https://support.microsoft.com/kb/2533623">KB2533623</see>
    /// to be installed.
    /// </para>
    /// <para>
    /// Windows Server 2003 and Windows XP: This value is not supported.
    /// </para>
    /// <para>
    /// Extension: "application directory" is the directory that contains the executable
    /// image of the calling process, not the current working directory. For hosted
    /// scenarios (tests, IIS, service hosts) this may not be where your managed assembly
    /// resides - combine with <see cref="LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR"/> when loading
    /// a library from a known fully qualified path.
    /// </para>
    /// </remarks>
    LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200,

    /// <summary>
    /// If this value is used, directories added using the <c>AddDllDirectory</c> or the
    /// <c>SetDllDirectory</c> function are searched for the DLL and its dependencies. If
    /// more than one directory has been added, the order in which the directories are
    /// searched is unspecified. Directories in the standard search path are not searched.
    /// This value cannot be combined with <see cref="LOAD_WITH_ALTERED_SEARCH_PATH"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows 7, Windows Server 2008 R2, Windows Vista and Windows Server 2008: This
    /// value requires <see href="https://support.microsoft.com/kb/2533623">KB2533623</see>
    /// to be installed.
    /// </para>
    /// <para>
    /// Windows Server 2003 and Windows XP: This value is not supported.
    /// </para>
    /// <para>
    /// Extension: prefer <c>AddDllDirectory</c> over <c>SetDllDirectory</c>. The latter
    /// mutates a global, non thread-safe string and also disables safe DLL search mode
    /// while the directory is set. <c>AddDllDirectory</c> returns a cookie that you can
    /// later remove with <c>RemoveDllDirectory</c>.
    /// </para>
    /// </remarks>
    LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400,

    /// <summary>
    /// If this value is used, <c>%windows%\system32</c> is searched for the DLL and its
    /// dependencies. Directories in the standard search path are not searched. This value
    /// cannot be combined with <see cref="LOAD_WITH_ALTERED_SEARCH_PATH"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows 7, Windows Server 2008 R2, Windows Vista and Windows Server 2008: This
    /// value requires <see href="https://support.microsoft.com/kb/2533623">KB2533623</see>
    /// to be installed.
    /// </para>
    /// <para>
    /// Windows Server 2003 and Windows XP: This value is not supported.
    /// </para>
    /// <para>
    /// Extension: always include this flag when loading Windows API DLLs by bare name
    /// (for example <c>"kernel32.dll"</c> or <c>"user32.dll"</c>). Without it an attacker
    /// can plant a same-named DLL next to your executable and the loader will prefer that
    /// copy. It is part of the default flag combination used by <see cref="UnmanagedLibrary"/>.
    /// </para>
    /// </remarks>
    LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800,

    /// <summary>
    /// This value is a combination of <see cref="LOAD_LIBRARY_SEARCH_APPLICATION_DIR"/>,
    /// <see cref="LOAD_LIBRARY_SEARCH_SYSTEM32"/>, and
    /// <see cref="LOAD_LIBRARY_SEARCH_USER_DIRS"/>. Directories in the standard search
    /// path are not searched. This value cannot be combined with
    /// <see cref="LOAD_WITH_ALTERED_SEARCH_PATH"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value represents the recommended maximum number of directories an application
    /// should include in its DLL search path.
    /// </para>
    /// <para>
    /// Windows 7, Windows Server 2008 R2, Windows Vista and Windows Server 2008: This
    /// value requires <see href="https://support.microsoft.com/kb/2533623">KB2533623</see>
    /// to be installed.
    /// </para>
    /// <para>
    /// Windows Server 2003 and Windows XP: This value is not supported.
    /// </para>
    /// <para>
    /// Extension: convenient one-liner for "safe" DLL resolution without the
    /// current-directory hazard. Pair with <see cref="LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR"/>
    /// when you pass a fully qualified path and want dependencies resolved next to the
    /// loaded module first.
    /// </para>
    /// </remarks>
    LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000,

    /// <summary>
    /// If this value is used, loading a DLL for execution from the current directory is
    /// only allowed if it is under a directory in the Safe load list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Extension: this flag is an extra guard against the classic "current directory DLL
    /// planting" attack. Even when the caller - or a transitive dependency - resolves a
    /// name from the current working directory, the loader will refuse to execute a module
    /// that does not live under a directory on the system's Safe load list. Prefer using
    /// this flag together with the <c>LOAD_LIBRARY_SEARCH_*</c> flags (especially
    /// <see cref="LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR"/> and
    /// <see cref="LOAD_LIBRARY_SEARCH_SYSTEM32"/>) when you must accept module names that
    /// a user or configuration file can influence.
    /// </para>
    /// </remarks>
    LOAD_LIBRARY_SAFE_CURRENT_DIRS = 0x00002000,
}
#pragma warning restore S2344, S2346
