using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Valheim_Serverside
{
	/*
		Which processors the server runs on, and at what priority.

		A game server often shares its host, and the operating system moves its threads freely over
		every core. On a chiplet CPU (AMD EPYC, Ryzen) the cores sit on separate dies, each with its
		own L3 cache and, on the first EPYC generation, its own memory controller: a thread moved to
		another die starts with a cold cache and reads its memory over the fabric. Keeping the whole
		process on one chiplet avoids that.

		The chiplets are read from the operating system's own topology, in this order: processor dies
		(Windows: GetLogicalProcessorInformationEx RelationProcessorDie; Linux: /sys topology die_id),
		else NUMA nodes when there is more than one, else the processors sharing an L3 cache -- never
		a group that spans two chiplets. "auto" takes the chiplet that was least busy over one second
		at startup; a list ("16-31,48") takes those processors. When the system reports nothing
		usable, nothing changes.

		Windows applies the mask to the whole process (SetProcessAffinityMask): every thread, also
		later ones, stays within it. Linux sets it on every thread there is (sched_setaffinity per
		thread); threads created later inherit their creator's, and the few that set their own are
		moved back 30 s and 5 min after startup. Processor groups beyond the first 64 processors are
		not used.
	*/
	public static class ProcessPlacement
	{
		private static readonly bool s_windows = Environment.OSVersion.Platform == PlatformID.Win32NT;

		// Called once at startup; "auto" is measured on a background thread so startup does not wait.
		public static void Apply(string affinity, string priority, Action<string> log)
		{
			affinity = (affinity ?? "").Trim();
			priority = (priority ?? "").Trim();
			if (priority.Length > 0 && !priority.Equals("Normal", StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					log(SetPriority(priority));
				}
				catch (Exception e)
				{
					log("Process priority failed, left as it was: " + e.Message);
				}
			}
			if (affinity.Length == 0)
			{
				return;
			}
			if (affinity.Equals("auto", StringComparison.OrdinalIgnoreCase))
			{
				new Thread(() =>
				{
					try
					{
						log(PlaceOnChiplet(out ulong placed));
						KeepPlaced(placed, log);
					}
					catch (Exception e)
					{
						log("Processor placement failed, left as it was: " + e.Message);
					}
				}) { IsBackground = true, Name = "ProcessPlacement" }.Start();
				return;
			}
			try
			{
				ulong mask = ParseList(affinity, strict: true);
				ulong all = AllProcessors();
				if (all != 0 && (mask & ~all) != 0)
				{
					log($"Processor placement: '{affinity}' names processors this system does not have ({Describe(all)}); left as it was.");
					return;
				}
				bool set = SetAffinity(mask);
				log(set ? $"Processor placement: processors {Describe(mask)}, as configured." : "Processor placement: the system refused the affinity; left as it was.");
				if (set && !s_windows)
				{
					new Thread(() => KeepPlaced(mask, log)) { IsBackground = true, Name = "ProcessPlacement" }.Start();
				}
			}
			catch (Exception e)
			{
				log($"Processor placement: '{affinity}' cannot be used ({e.Message}); left as it was.");
			}
		}

		/*
			Linux only: a thread that sets its own processors keeps them (Steam's starts three such
			threads a few seconds after the plugin has run), so the setting is checked again 30 s and
			5 min after startup and any thread off it is moved back. Windows needs none of this: no
			thread can run outside its process's mask.
		*/
		private static void KeepPlaced(ulong mask, Action<string> log)
		{
			if (s_windows || mask == 0)
			{
				return;
			}
			foreach (int seconds in new[] { 30, 270 })
			{
				Thread.Sleep(seconds * 1000);
				int moved = 0;
				ulong[] current = new ulong[16];
				foreach (int tid in EveryThread())
				{
					Array.Clear(current, 0, current.Length);
					// Only a thread allowed onto processors outside the mask; one kept to some of them chose that itself.
					if (sched_getaffinity(tid, new IntPtr(8 * current.Length), current) == 0 && (current[0] & ~mask) != 0
						&& sched_setaffinity(tid, new IntPtr(8), new[] { mask }) == 0)
					{
						moved++;
					}
				}
				if (moved > 0)
				{
					log($"Processor placement: {moved} thread(s) started since with processors of their own, moved back to {Describe(mask)}.");
				}
			}
		}

		private static string PlaceOnChiplet(out ulong placed)
		{
			placed = 0;
			string source;
			List<ulong> chiplets = Chiplets(out source);
			// Processors the process was already kept off (started with an affinity) stay out of it.
			ulong allowed = Allowed();
			if (chiplets != null && allowed != 0)
			{
				chiplets = chiplets.Select(c => c & allowed).Where(c => c != 0).Distinct().ToList();
			}
			if (chiplets == null || chiplets.Count < 2)
			{
				return $"Processor placement: the system reports {(chiplets == null ? "no" : "one")} chiplet ({source}{(allowed != 0 && allowed != AllProcessors() ? $", of processors {Describe(allowed)} it may use" : "")}); left as it was.";
			}
			double[] busy = Busy(1000);
			if (busy == null)
			{
				return "Processor placement: the system does not say how busy its processors are; left as it was.";
			}
			int best = -1;
			double bestBusy = double.MaxValue;
			for (int i = 0; i < chiplets.Count; i++)
			{
				double b = Average(busy, chiplets[i]);
				// Ties go to the later chiplet: the first one usually also handles the system's interrupts.
				if (b <= bestBusy)
				{
					bestBusy = b;
					best = i;
				}
			}
			ulong mask = chiplets[best];
			if (!SetAffinity(mask))
			{
				return "Processor placement: the system refused the affinity; left as it was.";
			}
			placed = mask;
			return $"Processor placement: chiplet {best + 1} of {chiplets.Count} ({source}), processors {Describe(mask)}, {100 * bestBusy:0}% busy over 1 s, the least of {chiplets.Count}.";
		}

		private static double Average(double[] busy, ulong mask)
		{
			double sum = 0;
			int n = 0;
			for (int cpu = 0; cpu < 64 && cpu < busy.Length; cpu++)
			{
				if ((mask & (1UL << cpu)) != 0)
				{
					sum += busy[cpu];
					n++;
				}
			}
			return n > 0 ? sum / n : 1;
		}

		// ---- topology

		private static List<ulong> Chiplets(out string source)
		{
			List<ulong> found;
			if (s_windows)
			{
				found = WindowsGroups(RelationProcessorDie, 0);
				if (Several(found)) { source = "processor dies"; return found; }
				found = WindowsGroups(RelationNumaNode, 0);
				if (Several(found)) { source = "NUMA nodes"; return found; }
				found = WindowsGroups(RelationCache, 3);
				source = "processors sharing an L3 cache";
				return found;
			}
			found = LinuxDies();
			if (Several(found)) { source = "processor dies"; return found; }
			found = LinuxList("/sys/devices/system/node", "node", "cpulist");
			if (Several(found)) { source = "NUMA nodes"; return found; }
			found = LinuxL3();
			source = "processors sharing an L3 cache";
			return found;
		}

		private static bool Several(List<ulong> groups) => groups != null && groups.Distinct().Count() > 1;

		private const int RelationNumaNode = 1, RelationCache = 2, RelationProcessorDie = 5;

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint returnedLength);

		// The processor-group-0 masks of every record of that kind (for caches: of that level).
		private static List<ulong> WindowsGroups(int relationship, int cacheLevel)
		{
			uint length = 0;
			GetLogicalProcessorInformationEx(relationship, IntPtr.Zero, ref length);
			if (length == 0)
			{
				return null;
			}
			IntPtr buffer = Marshal.AllocHGlobal((int)length);
			try
			{
				if (!GetLogicalProcessorInformationEx(relationship, buffer, ref length))
				{
					return null;
				}
				List<ulong> masks = new List<ulong>();
				for (int offset = 0; offset + 8 <= length;)
				{
					IntPtr record = IntPtr.Add(buffer, offset);
					int kind = Marshal.ReadInt32(record, 0);
					int size = Marshal.ReadInt32(record, 4);
					if (size <= 0)
					{
						break;
					}
					if (kind == relationship && (kind != RelationCache || Marshal.ReadByte(record, 8) == cacheLevel))
					{
						// GroupCount and the GROUP_AFFINITY array: after 22 bytes for a processor or NUMA record, 30 for a cache.
						int countAt = kind == RelationCache ? 38 : 30;
						int groups = Math.Max(1, (int)Marshal.ReadInt16(record, countAt));
						ulong mask = 0;
						for (int g = 0; g < groups; g++)
						{
							int at = countAt + 2 + 16 * g;
							if (Marshal.ReadInt16(record, at + 8) == 0)
							{
								mask |= (ulong)Marshal.ReadInt64(record, at);
							}
						}
						if (mask != 0)
						{
							masks.Add(mask);
						}
					}
					offset += size;
				}
				return masks;
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
		}

		private static List<ulong> LinuxDies()
		{
			Dictionary<string, ulong> dies = new Dictionary<string, ulong>();
			foreach (int cpu in LinuxCpus())
			{
				string topology = $"/sys/devices/system/cpu/cpu{cpu}/topology/";
				if (!File.Exists(topology + "die_id"))
				{
					return null;
				}
				string die = File.ReadAllText(topology + "die_id").Trim();
				string key = File.ReadAllText(topology + "physical_package_id").Trim() + "/" + die;
				dies[key] = (dies.TryGetValue(key, out ulong m) ? m : 0) | (cpu < 64 ? 1UL << cpu : 0);
			}
			return dies.Values.ToList();
		}

		private static List<ulong> LinuxL3()
		{
			HashSet<ulong> groups = new HashSet<ulong>();
			foreach (int cpu in LinuxCpus())
			{
				string cache = $"/sys/devices/system/cpu/cpu{cpu}/cache";
				if (!Directory.Exists(cache))
				{
					continue;
				}
				foreach (string index in Directory.GetDirectories(cache, "index*"))
				{
					if (File.Exists(index + "/level") && File.ReadAllText(index + "/level").Trim() == "3")
					{
						groups.Add(ParseList(File.ReadAllText(index + "/shared_cpu_list").Trim()));
					}
				}
			}
			return groups.ToList();
		}

		private static List<ulong> LinuxList(string dir, string prefix, string file)
		{
			if (!Directory.Exists(dir))
			{
				return null;
			}
			return Directory.GetDirectories(dir, prefix + "*")
				.Where(d => File.Exists(Path.Combine(d, file)))
				.Select(d => ParseList(File.ReadAllText(Path.Combine(d, file)).Trim()))
				.Where(m => m != 0).ToList();
		}

		// The processors that are online (an offline one has no topology and cannot be used).
		private static IEnumerable<int> LinuxCpus()
		{
			IEnumerable<int> all = Directory.GetDirectories("/sys/devices/system/cpu", "cpu*")
				.Select(d => Path.GetFileName(d).Substring(3))
				.Where(n => n.Length > 0 && n.All(char.IsDigit))
				.Select(int.Parse).OrderBy(n => n);
			const string online = "/sys/devices/system/cpu/online";
			if (!File.Exists(online))
			{
				return all;
			}
			ulong mask = ParseList(File.ReadAllText(online).Trim());
			return all.Where(cpu => cpu >= 64 || (mask & (1UL << cpu)) != 0);
		}

		// ---- load

		[DllImport("ntdll.dll")]
		private static extern int NtQuerySystemInformation(int informationClass, IntPtr information, int length, out int returnLength);

		// Busy share of each processor over the given time, or null if the system does not say.
		private static double[] Busy(int milliseconds)
		{
			ulong[,] first = Times();
			if (first == null)
			{
				return null;
			}
			Thread.Sleep(milliseconds);
			ulong[,] second = Times();
			if (second == null || second.GetLength(0) != first.GetLength(0))
			{
				return null;
			}
			double[] busy = new double[first.GetLength(0)];
			for (int cpu = 0; cpu < busy.Length; cpu++)
			{
				double total = second[cpu, 0] - first[cpu, 0];
				double idle = second[cpu, 1] - first[cpu, 1];
				busy[cpu] = total > 0 ? Math.Max(0, Math.Min(1, 1 - idle / total)) : 0;
			}
			return busy;
		}

		// Per processor: total time and idle time, in the system's own units.
		private static ulong[,] Times()
		{
			if (s_windows)
			{
				// SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION: idle, kernel (with idle), user, ... 48 bytes each.
				int size = 48 * 64;
				IntPtr buffer = Marshal.AllocHGlobal(size);
				try
				{
					if (NtQuerySystemInformation(8, buffer, size, out int returned) != 0 || returned < 48)
					{
						return null;
					}
					int n = returned / 48;
					ulong[,] times = new ulong[n, 2];
					for (int i = 0; i < n; i++)
					{
						ulong idle = (ulong)Marshal.ReadInt64(buffer, 48 * i);
						ulong kernel = (ulong)Marshal.ReadInt64(buffer, 48 * i + 8);
						ulong user = (ulong)Marshal.ReadInt64(buffer, 48 * i + 16);
						times[i, 0] = kernel + user;
						times[i, 1] = idle;
					}
					return times;
				}
				finally
				{
					Marshal.FreeHGlobal(buffer);
				}
			}
			if (!File.Exists("/proc/stat"))
			{
				return null;
			}
			List<string> lines = File.ReadAllLines("/proc/stat").Where(l => l.StartsWith("cpu") && l.Length > 3 && char.IsDigit(l[3])).ToList();
			int count = lines.Select(l => int.Parse(l.Split(' ')[0].Substring(3))).DefaultIfEmpty(-1).Max() + 1;
			if (count <= 0)
			{
				return null;
			}
			ulong[,] linux = new ulong[count, 2];
			foreach (string line in lines)
			{
				string[] f = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				int cpu = int.Parse(f[0].Substring(3));
				ulong[] v = f.Skip(1).Take(8).Select(x => ulong.Parse(x, CultureInfo.InvariantCulture)).ToArray();
				linux[cpu, 0] = v.Aggregate(0UL, (a, b) => a + b);
				linux[cpu, 1] = v[3] + (v.Length > 4 ? v[4] : 0); // idle + iowait
			}
			return linux;
		}

		// ---- applying

		[DllImport("kernel32.dll")]
		private static extern IntPtr GetCurrentProcess();

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool SetProcessAffinityMask(IntPtr process, UIntPtr mask);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetProcessAffinityMask(IntPtr process, out UIntPtr processMask, out UIntPtr systemMask);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool SetPriorityClass(IntPtr process, uint priorityClass);

		[DllImport("libc", SetLastError = true)]
		private static extern int sched_setaffinity(int pid, IntPtr cpusetsize, ulong[] mask);

		[DllImport("libc", SetLastError = true)]
		private static extern int setpriority(int which, int who, int prio);

		[DllImport("libc", SetLastError = true)]
		private static extern int sched_getaffinity(int pid, IntPtr cpusetsize, ulong[] mask);

		// The processors this process may run on now (the first 64), or 0 if the system does not say.
		private static ulong Allowed()
		{
			if (s_windows)
			{
				return GetProcessAffinityMask(GetCurrentProcess(), out UIntPtr process, out _) ? (ulong)process : 0;
			}
			ulong[] mask = new ulong[16];
			return sched_getaffinity(0, new IntPtr(8 * mask.Length), mask) == 0 ? mask[0] : 0;
		}

		private static ulong AllProcessors()
		{
			if (s_windows)
			{
				return GetProcessAffinityMask(GetCurrentProcess(), out _, out UIntPtr system) ? (ulong)system : 0;
			}
			return LinuxCpus().Where(c => c < 64).Aggregate(0UL, (m, c) => m | 1UL << c);
		}

		private static bool SetAffinity(ulong mask)
		{
			if (s_windows)
			{
				return SetProcessAffinityMask(GetCurrentProcess(), new UIntPtr(mask));
			}
			bool any = false;
			foreach (int tid in EveryThread())
			{
				if (sched_setaffinity(tid, new IntPtr(8), new[] { mask }) == 0)
				{
					any = true;
				}
			}
			return any;
		}

		/*
			Every thread of this process, each once. A thread started meanwhile by one not yet reached
			would keep the old setting, so the list is read again until a pass finds no new thread.
		*/
		private static IEnumerable<int> EveryThread()
		{
			HashSet<int> seen = new HashSet<int>();
			for (int pass = 0; pass < 20; pass++)
			{
				bool found = false;
				foreach (string task in Directory.GetDirectories("/proc/self/task"))
				{
					if (int.TryParse(Path.GetFileName(task), out int tid) && seen.Add(tid))
					{
						found = true;
						yield return tid;
					}
				}
				if (!found)
				{
					yield break;
				}
			}
		}

		private static string SetPriority(string priority)
		{
			if (s_windows)
			{
				uint value;
				switch (priority.ToLowerInvariant())
				{
					case "abovenormal": value = 0x8000; break;
					case "high": value = 0x80; break;
					default: return $"Process priority: '{priority}' is not Normal, AboveNormal or High; left as it was.";
				}
				return SetPriorityClass(GetCurrentProcess(), value) ? $"Process priority: {priority}." : "Process priority: the system refused it; left as it was.";
			}
			int nice;
			switch (priority.ToLowerInvariant())
			{
				case "abovenormal": nice = -5; break;
				case "high": nice = -10; break;
				default: return $"Process priority: '{priority}' is not Normal, AboveNormal or High; left as it was.";
			}
			int done = 0, tasks = 0;
			foreach (int tid in EveryThread())
			{
				tasks++;
				if (setpriority(0, tid, nice) == 0)
				{
					done++;
				}
			}
			return done > 0 ? $"Process priority: {priority} (nice {nice}) on {done} of {tasks} thread(s)." : "Process priority: the system refused it (raising priority on Linux needs CAP_SYS_NICE); left as it was.";
		}

		// ---- processor lists

		/*
			"0-7,16,32-39" -> mask. The system's own lists may name processors 64 and above, which are
			left out; a list from the config (strict) must be well-formed and name only processors 0-63.
		*/
		public static ulong ParseList(string list, bool strict = false)
		{
			ulong mask = 0;
			foreach (string part in list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string[] range = part.Trim().Split('-');
				if (range.Length > 2)
				{
					throw new FormatException($"'{part.Trim()}' is not a processor or a range of them");
				}
				int from = int.Parse(range[0].Trim(), CultureInfo.InvariantCulture);
				int to = range.Length > 1 ? int.Parse(range[1].Trim(), CultureInfo.InvariantCulture) : from;
				if (strict && (from > to || to > 63))
				{
					throw new FormatException(from > to ? $"'{part.Trim()}' runs backwards" : $"'{part.Trim()}' goes past processor 63, the last one this can use");
				}
				for (int cpu = from; cpu <= to && cpu < 64; cpu++)
				{
					mask |= 1UL << cpu;
				}
			}
			if (strict && mask == 0)
			{
				throw new FormatException("it names no processor");
			}
			return mask;
		}

		public static string Describe(ulong mask)
		{
			List<string> parts = new List<string>();
			for (int cpu = 0; cpu < 64; cpu++)
			{
				if ((mask & (1UL << cpu)) == 0)
				{
					continue;
				}
				int end = cpu;
				while (end + 1 < 64 && (mask & (1UL << (end + 1))) != 0)
				{
					end++;
				}
				parts.Add(end > cpu ? $"{cpu}-{end}" : cpu.ToString());
				cpu = end;
			}
			return parts.Count > 0 ? string.Join(",", parts) : "none";
		}
	}
}
