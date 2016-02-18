/*  $Id$
*  
*  Project: Swicli.Library - Two Way Interface for .NET and MONO to SWI-Prolog
*  Author:        Douglas R. Miles
*  E-mail:        logicmoo@gmail.com
*  WWW:           http://www.logicmoo.com
*  Copyright (C):  2010-2012 LogicMOO Developement
*
*  This library is free software; you can redistribute it and/or
*  modify it under the terms of the GNU Lesser General Public
*  License as published by the Free Software Foundation; either
*  version 2.1 of the License, or (at your option) any later version.
*
*  This library is distributed in the hope that it will be useful,
*  but WITHOUT ANY WARRANTY; without even the implied warranty of
*  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
*  Lesser General Public License for more details.
*
*  You should have received a copy of the GNU Lesser General Public
*  License along with this library; if not, write to the Free Software
*  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
*
*********************************************************/
#if USE_IKVM
using IKVM.Internal;
using ikvm.runtime;
using java.net;
using jpl;
#endif
#if USE_IKVM
using Hashtable = java.util.Hashtable;
using ClassLoader = java.lang.ClassLoader;
using Class = java.lang.Class;
using sun.reflect.misc;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using SbsSW.SwiPlCs;
using SbsSW.SwiPlCs.Callback;
using PlTerm = SbsSW.SwiPlCs.PlTerm;

namespace Swicli.Library
{
    public partial class PrologCLR
    {

        /// <summary>
        /// the .Net process (Not OS)
        /// </summary>
        /// <returns></returns>
        internal static bool Is64BitRuntime()
        {
            int bits = IntPtr.Size * 8;
            return bits == 64;
        }

        /// <summary>
        /// The OS and not the .Net process
        ///  therefore "Program Files" are either for 64bit or 32bit apps
        /// </summary>
        /// <returns></returns>
        internal static bool Is64BitComputer()
        {
            return Is64BitRuntime() || !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("ProgramFiles(x86)"));
        }

        static public void load_swiplcs()
        {

        }

        private static readonly object PrologIsSetupLock = new object();
        private static bool PrologIsSetup;
        public static void SetupProlog()
        {
            lock (PrologIsSetupLock)
            {
                if (PrologIsSetup) return;
                AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
                PrologIsSetup = true;
                SafelyRun(SetupProlog0);               
                SafelyRun(SetupProlog2);
                RegisterPLCSForeigns();
            }
        }
        public static void SetupProlog0()
        {
            PrologCLR.Debug("SetupProlog");
            //  SafelyRun(SetupIKVM);
            if (!IsUseableSwiProlog(SwiHomeDir))
            {
                try
                {
                    //SwiHomeDir = System.Windows.Forms.Application.StartupPath;
                    SwiHomeDir = Path.GetDirectoryName(Environment.CurrentDirectory);
                    // CommandLine.Trim(' ', '"', '\''));
                    if (!IsUseableSwiProlog(SwiHomeDir))
                    {
                        SwiHomeDir = null;
                    }
                }
                catch (Exception)
                {
                }
            }
            if (!IsUseableSwiProlog(SwiHomeDir))
            {
                SwiHomeDir = Environment.GetEnvironmentVariable("SWI_HOME_DIR");
                if (!IsUseableSwiProlog(SwiHomeDir))
                {
                    SwiHomeDir = null;
                }
            }
            string pf = GetProgramFilesDir();
            if (!IsUseableSwiProlog(SwiHomeDir))
            {

                SwiHomeDir = pf + "/pl";
                if (!IsUseableSwiProlog(SwiHomeDir))
                {
                    SwiHomeDir = pf + "/swipl";
                }
            }
            AltSwiHomeDir = AltSwiHomeDir ?? ".";
            bool copyPlatFormVersions = false;
            if (!IsUseableSwiProlog(SwiHomeDir))
            {
                SwiHomeDir = AltSwiHomeDir;
                copyPlatFormVersions = true;
                if (true)
                {
                    //for now never copy!
                    copyPlatFormVersions = false;
                }
            }
            SwiHomeDir = SwiHomeDir ?? AltSwiHomeDir;
            if (IsUseableSwiProlog(SwiHomeDir))
            {
                Environment.SetEnvironmentVariable("SWI_HOME_DIR", SwiHomeDir);
            }
            if (!ConfirmRCFile(SwiHomeDir)) ConsoleTrace("RC file missing from " + SwiHomeDir);
            string platformSuffix = Is64BitRuntime() ? "-x64" : "-x86";
            if (copyPlatFormVersions)
            {
                string destination = Path.Combine(SwiHomeDir, "bin");
                CopyFiles(destination + platformSuffix, destination, true, "*.*", true);
                destination = Path.Combine(SwiHomeDir, "lib");
                CopyFiles(destination + platformSuffix, destination, true, "*.*", true);
            }

            if (IsUseableSwiProlog(SwiHomeDir))
            {
                Environment.SetEnvironmentVariable("SWI_HOME_DIR", SwiHomeDir);
            }

            SafelyRun(SetupProlog1);
        }

        private static string GetProgramFilesDir()
        {
            if (Is64BitComputer())
            {
                if (!Is64BitRuntime())
                {
                    return Environment.GetEnvironmentVariable("ProgramFiles(x86)");
                }
            }
            return Environment.GetEnvironmentVariable("ProgramFiles");
        }

        private static string GetMyPathExtras()
        {
            if (libpath != null) return libpath;
            string swiHomeBin = Path.Combine(SwiHomeDir, "bin");
            libpath += swiHomeBin;
            if (swiHomeBin != IKVMHome && !string.IsNullOrEmpty(IKVMHome))
            {
                libpath += Path.PathSeparator;
                libpath += IKVMHome;
            }
            libpath += Path.PathSeparator;
            libpath += ".";
            return libpath;
        }
        /// <summary>
        /// This after the SwiPrologDir and IKVMHome is set up will update the environment
        /// </summary>
        public static void SetupProlog1()
        {             
            string myPathExt = GetMyPathExtras();
            String path = Environment.GetEnvironmentVariable("PATH");
            if (path != null)
            {
                if (!path.ToLower().StartsWith(myPathExt.ToLower()))
                {
                    path = myPathExt + path;
                    Environment.SetEnvironmentVariable("PATH", path);
                }
            }
            
            string LD_LIBRARY_PATH = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            if (String.IsNullOrEmpty(LD_LIBRARY_PATH))
            {
                LD_LIBRARY_PATH = libpath;
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", LD_LIBRARY_PATH);
            }
            else if (!LD_LIBRARY_PATH.ToLower().Contains(myPathExt.ToLower()))
            {
                LD_LIBRARY_PATH = myPathExt + LD_LIBRARY_PATH;
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", LD_LIBRARY_PATH);
            }
            // SafelyRun(SetupProlog1);
            // SafelyRun(SetupProlog2);
        }
#if USE_IKVM
        public static void SetupProlog1()
        {
            PrologCLR.ConsoleTrace("geting lib path");
            CLASSPATH = java.lang.System.getProperty("java.class.path");
            ConsoleTrace("GOT lib path");
            string CLASSPATH0 = Environment.GetEnvironmentVariable("CLASSPATH");

            if (String.IsNullOrEmpty(CLASSPATH))
            {
                CLASSPATH = CLASSPATH0;
            }
            string jplcp = clasPathOf(new jpl.JPL());

            if (!JplDisabled)
                CLASSPATH = IKVMHome + "/SWIJPL.dll" + ";" + IKVMHome + "/SWIJPL.jar;" + CLASSPATH0;

            ConsoleWriteLine("CLASSPATH=" + CLASSPATH);
            if (CLASSPATH != null)
            {
                Environment.SetEnvironmentVariable("CLASSPATH", CLASSPATH);
                java.lang.System.setProperty("java.class.path", CLASSPATH);
            }
            java.lang.System.setProperty("java.library.path", libpath);
        }
#endif
        static string CLASSPATH = null;
        static string libpath = null;
        public static void SetupProlog2()
        {
            try
            {
#if USE_IKVM
                if (!JplDisabled)
                {
                    JPL.setNativeLibraryDir(SwiHomeDir + "/bin");
                    try
                    {
                        JPL.loadNativeLibrary();
                    }
                    catch (Exception e)
                    {
                        WriteException(e);
                        JplDisabled = true;
                    }
                    if (!JplDisabled)
                    {
                        SafelyRun(() => jpl.fli.Prolog.initialise());
                    }
                }
                SafelyRun(TestClassLoader);
#endif
                //if (IsPLWin) return;
                try
                {
                    if (!PlEngine.IsInitialized)
                    {
                        String[] param = { "-q" }; // suppressing informational and banner messages
                        PlEngine.Initialize(param);
                    }
                    if (IsPLWin) return;
                    if (!PlEngine.IsStreamFunctionReadModified) PlEngine.SetStreamReader(Sread);
                    PlQuery.PlCall("nl.");
                }
                catch (Exception e)
                {
                    WriteException(e);
                    PlCsDisabled = true;
                }
                //                PlAssert("jpl:jvm_ready");
                //                PlAssert("module_transparent(jvm_ready)");
            }
            catch (Exception exception)
            {
                WriteException(exception);
                return;
            }
        }

        private static bool IsUseableSwiProlog(string swiHomeDir)
        {
            if (string.IsNullOrEmpty(swiHomeDir)) return false;
            if (!Directory.Exists(swiHomeDir)) return false;
            string swibin = Path.Combine(swiHomeDir, "bin");
            if (File.Exists(Path.Combine(swibin, "libpl.dll")))
            {
                ConsoleTrace("SWI too old: " + swiHomeDir + "/bin/libpl.dll");
                return false;
            }
            if (!ConfirmRCFile(swiHomeDir)) return false;
            if (File.Exists(Path.Combine(swibin, "libswipl.dll"))) return true;
            if (File.Exists(Path.Combine(swibin, "swipl.dll"))) return true;
            return true;
        }

        private static bool ConfirmRCFile(string swiHomeDir)
        {
            if (!Is64BitRuntime())
            {
                return File.Exists(Path.Combine(swiHomeDir, "boot32.prc")) ||
                       File.Exists(Path.Combine(swiHomeDir, "boot.prc"));
            }
            return File.Exists(Path.Combine(swiHomeDir, "boot64.prc"));
        }

        //FileInfo & DirectoryInfo are in System.IO
        //This is something you should be able to tweak to your specific needs.
        static void CopyFiles(string source,
                              string destination,
                              bool overwrite,
                              string searchPattern, bool recurse)
        {
            if (Directory.Exists(source))
                CopyFiles(new DirectoryInfo(source), new DirectoryInfo(destination), overwrite, searchPattern, recurse);
        }

        static void CopyFiles(DirectoryInfo source,
                              DirectoryInfo destination,
                              bool overwrite,
                              string searchPattern, bool recurse)
        {
            FileInfo[] files = source.GetFiles(searchPattern);
            if (!destination.Exists)
            {
                destination.Create();
            }
            foreach (FileInfo file in files)
            {
                string destName = Path.Combine(destination.FullName, file.Name);
                try
                {
                    file.CopyTo(destName, overwrite);
                }
                catch (Exception e0)
                {
                    if (!overwrite)
                    {
                        ConsoleWriteLine("file: " + file + " copy to " + destName + " " + e0);
                    }
                    else
                    {
                        try
                        {
                            if (File.Exists(destName))
                            {
                                if (File.Exists(destName + ".dead")) File.Delete(destName + ".dead");
                                File.Move(destName, destName + ".dead");
                                file.CopyTo(destName, false);
                            }
                        }
                        catch (Exception e)
                        {
                            ConsoleWriteLine("file: " + file + " copy to " + destName + " " + e);
                        }
                    }
                }
            }
            if (recurse)
            {
                foreach (var info in source.GetDirectories())
                {
                    string destName = Path.Combine(destination.FullName, info.Name);
                    try
                    {
                        if (!Directory.Exists(destName)) Directory.CreateDirectory(destName);
                        CopyFiles(info, new DirectoryInfo(destName), overwrite, searchPattern, recurse);
                    }
                    catch (Exception e)
                    {
                        ConsoleWriteLine("file: " + info + " copy to " + destName + " " + e);
                    }
                }
            }
        }

        //[MTAThread]
        public static void Main(string[] args0)
        {
            while (false)
            {
                System.Diagnostics.Process process = new System.Diagnostics.Process();
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                startInfo.UseShellExecute = false;
                startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Maximized;
                startInfo.FileName = @"c:\pf\swipl\bin\swipl-win.exe";
                startInfo.Arguments = "winapi_dll.pl";
                startInfo.WorkingDirectory = @"C:\Users\Administrator\AppData\Roaming\SWI-Prolog\pack\swicli\cffi-tests";
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
            }
            PingThreadFactories();
            bool demo = false;
            SetupProlog();
            libpl.PL_initialise(args0.Length, args0);
            //Main12(args0);
            libpl.PL_toplevel();
        }


        //[MTAThread]
        public static void Main_Was(string[] args0)
        {
            PingThreadFactories();
            bool demo = false;
            SetupProlog();

            if (demo)
            {
                DoQuery("asserta(fff(1))");
                DoQuery("asserta(fff(9))");
                DoQuery("nl");
                DoQuery("flush");

                PlAssert("father(martin, inka)");
                if (!PlCsDisabled)
                {
                    PlQuery.PlCall("assert(father(uwe, gloria))");
                    PlQuery.PlCall("assert(father(uwe, melanie))");
                    PlQuery.PlCall("assert(father(uwe, ayala))");
                    using (PlQuery q = new PlQuery("father(P, C), atomic_list_concat([P,' is_father_of ',C], L)"))
                    {
                        foreach (PlTermV v in q.Solutions)
                            ConsoleTrace(ToCSString(v));

                        foreach (PlQueryVariables v in q.SolutionVariables)
                            ConsoleTrace(v["L"].ToString());


                        ConsoleTrace("all child's from uwe:");
                        q.Variables["P"].Unify("uwe");
                        foreach (PlQueryVariables v in q.SolutionVariables)
                            ConsoleTrace(v["C"].ToString());
                    }
                    //PlQuery.PlCall("ensure_loaded(library(thread_util))");
                    //Warning: [Thread 2] Thread running "thread_run_interactor" died on exception: thread_util:attach_console/0: Undefined procedure: thread_util:win_open_console/5
                    //PlQuery.PlCall("interactor");
                    //Delegate Foo0 = foo0;
                    RegisterPLCSForeigns();
                }

                PlAssert("tc2:-foo2(X,Y),writeq(f(X,Y)),nl,X=5");
                PlAssert("tc3:-foo3(X,Y,Z),Z,writeln(f(X,Y,Z)),X=5");
            }

#if USE_IKVM
            ClassFile.ThrowFormatErrors = false;
            libpl.NoToString = true;
            //SafelyRun((() => PlCall("jpl0")));            
            //SafelyRun((() => DoQuery(new Query(new jpl.Atom("jpl0")))));
            libpl.NoToString = false;
            ClassFile.ThrowFormatErrors = true;
#endif
            if (args0.Length > 0)
            {
                int i = 0;
                foreach (var s in args0)
                {
                    if (s == "-f")
                    {
                        string s1 = args0[i + 1];
                        args0[i + 1] = "['" + s1 + "']";
                        continue;
                    }
                    PlCall(s);
                    i++;
                }
            }
            if (!JplDisabled)
            {
#if USE_IKVM
                var run = new jpl.Atom("prolog");
                while (!IsHalted) SafelyRun(() => DoQuery(new jpl.Query(run)));
#endif
            }
            else
            {
                if (!PlCsDisabled)
                    // loops on exception
                    while (!SafelyRun(() => libpl.PL_toplevel())) ;
            }



            ConsoleTrace("press enter to exit");
            Console.ReadLine();
            SafelyRun(() => PlEngine.PlCleanup());

            ConsoleTrace("finshed!");


        }

        private static bool SafelyRun(Action invoker)
        {
            try
            {
                invoker();
                return true;
            }
            catch (Exception e)
            {
                WriteException(e);
                return false;
            }
        }

        public static bool VerboseStartup = false;
        public static void RegisterPLCSForeigns()
        {
            CreatorThread = Thread.CurrentThread;
            RegisterMainThread();
            ShortNameType = new Dictionary<string, Type>();
            ShortNameType["string"] = typeof(String);
            ShortNameType["object"] = typeof(Object);
            ShortNameType["sbyte"] = typeof(sbyte);

            // libpl.PL_agc_hook(new AtomGCHook(Tracker_FreeAtom));

            //ShortNameType = new PrologBackedDictionary<string, Type>(null, "shortTypeName");
            //PlEngine.RegisterForeign(null, "cliFindClass", 2, new DelegateParameter2(PrologCli.cliFindClass), PlForeignSwitches.None);
            PlEngine.RegisterForeign(ExportModule, "cli_load_assembly", 1, new DelegateParameter1(PrologCLR.cliLoadAssembly), PlForeignSwitches.None);
            if (VerboseStartup) ConsoleWriteLine("RegisterPLCSForeigns");
            InternMethod(null, "cwl", typeof(Console).GetMethod("WriteLine", ONE_STRING));

            Type t = typeof(PrologCLR);
            InternMethod(ExportModule, "cli_load_assembly_methods", t.GetMethod("cliLoadAssemblyMethods"));
            InternMethod(t.GetMethod("cliAddAssemblySearchPath"), "cli_");
            InternMethod(t.GetMethod("cliRemoveAssemblySearchPath"), "cli_");
            AddForeignMethods(t, false, "cli_");
            RegisterJPLForeigns();
            if (VerboseStartup) ConsoleWriteLine("done RegisterPLCSForeigns");
        }


        private static void RegisterJPLForeigns()
        {
            // backup old jpl.pl and copy over it
            if (!JplDisabled)
                SafelyRun(() =>
                              {
                                  if (File.Exists(IKVMHome + "/jpl_for_ikvm.phps"))
                                  {
                                      if (!File.Exists(SwiHomeDir + "/library/jpl.pl.old"))
                                      {
                                          File.Copy(SwiHomeDir + "/library/jpl.pl",
                                                    SwiHomeDir + "/library/jpl.pl.old",
                                                    true);
                                      }
                                      File.Copy(IKVMHome + "/jpl_for_ikvm.phps", SwiHomeDir + "/library/jpl.pl", true);
                                  }
                              });

            PlEngine.RegisterForeign("swicli", "link_swiplcs", 1, new DelegateParameter1(link_swiplcs),
                                     PlForeignSwitches.None);
            //JplSafeNativeMethods.install();
            JplSafeNativeMethodsCalled = true;
            //DoQuery(new Query("ensure_loaded(library(jpl))."));
            /*
                             
             
jpl_jlist_demo :-
	jpl_new( 'javax.swing.JFrame', ['modules'], F),
	jpl_new( 'javax.swing.DefaultListModel', [], DLM),
	jpl_new( 'javax.swing.JList', [DLM], L),
	jpl_call( F, getContentPane, [], CP),
	jpl_call( CP, add, [L], _),
	(	current_module( M),
		jpl_call( DLM, addElement, [M], _),
		fail
	;	true
	),
	jpl_call( F, pack, [], _),
	jpl_call( F, getHeight, [], H),
	jpl_call( F, setSize, [150,H], _),
	jpl_call( F, setVisible, [@(true)], _).


% this directive runs the above demo

:- jpl_jlist_demo.

             */
            return; //we dont need to really do this
            PlCall("use_module(library(jpl)).");
            PlAssert("jpl0 :- jpl_new( 'java.lang.String', ['hi'], DLM),writeln(DLM)");
            PlAssert("jpl1 :- jpl_new( 'javax.swing.DefaultListModel', [], DLM),writeln(DLM)");
        }

        private static bool link_swiplcs(PlTerm pathName)
        {
            try
            {
                return true;
                if (JplSafeNativeMethodsCalled)
                {
                    bool enabled = !JplSafeNativeMethodsDisabled;
                    SafelyRun(
                        () => ConsoleTrace("JplSafeNativeMethods called again from " + pathName + " result=" + enabled));
                    return enabled;
                }
                JplSafeNativeMethodsCalled = true;
                SafelyRun(() => ConsoleTrace("JplSafeNativeMethods call first time from " + pathName));
                JplSafeNativeMethods.install();
                //var v = new PlTerm("_1");
                //JplSafeNativeMethods.jpl_c_lib_version_1_plc(v.TermRef);
                return true;
            }
            catch (Exception e)
            {
                JplSafeNativeMethodsDisabled = true;
                WriteException(e);
                return false;
            }
        }
        private static void FooMethod(String print)
        {
            //DoQuery(new Query("asserta(jpl:jvm_ready)."));
            //DoQuery(new Query("asserta(jpl:jpl_c_lib_version(3-3-3-3))."));

            //DoQuery(new Query("module(jpl)."));
            //JplSafeNativeMethods.install();
            //DoQuery("ensure_loaded(library(jpl)).");
            //DoQuery("module(user).");
            //DoQuery(new Query("load_foreign_library(foreign(jpl))."));
            // DoQuery(new Query(new jpl.Compound("member", new Term[] { new jpl.Integer(1), new jpl.Variable("H") })));
            //DoQuery(new Query(new jpl.Atom("interactor")));
            //DoQuery(new Query(new jpl.Compound("writeq", new Term[] { new jpl.Integer(1) })));

            ConsoleTrace(print);
        }

        static internal long Sread(IntPtr handle, System.IntPtr buffer, long buffersize)
        {
            int i = Console.Read();
            if (i == -1) return 0;
            string s = "" + (char)i;
            byte[] array = System.Text.Encoding.Unicode.GetBytes(s);
            System.Runtime.InteropServices.Marshal.Copy(array, 0, buffer, array.Length);
            return array.Length;
        }



        public static bool JplDisabled = true;
        public static bool PlCsDisabled = false;
        private static string _ikvmHome = ".";
        public static string IKVMHome
        {
            get { return _ikvmHome; }
            set { _ikvmHome = RemoveTrailingPathSeps(value); }
        }

        private static string _swiHomeDir;// = Path.Combine(".", "swiprolog");
        public static string SwiHomeDir
        {
            get { return _swiHomeDir; }
            set
            {
                _swiHomeDir = RemoveTrailingPathSeps(value); ;
            }
        }

        private static string RemoveTrailingPathSeps(string value)
        {
            if (value != null)
            {
                value = value.TrimEnd("\\/".ToCharArray()).Replace("\\", "/");
            }
            return value;
        }


        public static string AltSwiHomeDir = ".";//C:/development/opensim4opencog";// Path.Combine(".", "swiprolog");
        public static bool JplSafeNativeMethodsDisabled = false;
        public static bool JplSafeNativeMethodsCalled = false;
        public static bool IsHalted = false;
        private static Int64 TopOHandle = 6660000;
        private static readonly Dictionary<string, object> SavedDelegates = new Dictionary<string, object>();
        public static bool FailOnMissingInsteadOfError = true;
        public static Thread CreatorThread;
        public static bool IsPLWin;
        public static bool RedirectStreams = false;
        public static int VMStringsAsAtoms = libpl.CVT_STRING;

        public static bool IsLinux = Type.GetType("Mono.Runtime") != null;

        public void InitFromUser()
        {
            ConsultIfExists("prolog/cogbot.pl");
        }

    }

    [System.Security.SuppressUnmanagedCodeSecurity]
    public static class JplSafeNativeMethods
    {
        //private const string DllFileName = @"D:\Lesta\swi-pl\pl\bin\LibPl.dll";
        private const string DllFileName = @"jpl.dll";//"libpl.dll" for 5.7.8; //was 

        public static string DllFileName1
        {
            get { return DllFileName; }
        }
        [DllImport(DllFileName)]
        public static extern void install();

        //[DllImport(DllFileName)]
        //public static extern java.lang.Thread jni_env();

        //[DllImport(DllFileName)]
        //public static extern int jpl_c_lib_version_1_plc(uint term_t);
    }

}