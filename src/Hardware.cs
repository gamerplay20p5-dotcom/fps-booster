using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace F26Boost
{
    internal sealed class Hardware
    {
        public string Machine = "Não identificado", Cpu = "Não identificado";
        public int Cores, Threads = Environment.ProcessorCount, MemoryModules;
        public ulong TotalRam, AvailableRam;
        public bool Laptop, OnBattery, PowerUnknown;
        public readonly List<string> Gpus = new List<string>();
        public readonly List<string> Disks = new List<string>();
        public bool DedicatedGpu;
        public double RamGb { get { return TotalRam / 1073741824.0; } }

        [StructLayout(LayoutKind.Sequential)]
        private struct PowerStatus
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public uint BatteryLifeTime, BatteryFullLifeTime;
        }
        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out PowerStatus status);

        public static Hardware Detect()
        {
            var mem = Native.GetMemory();
            var h = new Hardware { TotalRam = mem.ullTotalPhys, AvailableRam = mem.ullAvailPhys };
            Query("root\\cimv2", "SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem", o => {
                h.Machine = Convert.ToString(o["Manufacturer"]) + " " + Convert.ToString(o["Model"]);
                h.Laptop = Convert.ToInt32(o["PCSystemType"]) == 2;
            });
            Query("root\\cimv2", "SELECT Name, NumberOfCores FROM Win32_Processor", o => {
                h.Cpu = Convert.ToString(o["Name"]).Trim(); h.Cores += Convert.ToInt32(o["NumberOfCores"]);
            });
            Query("root\\cimv2", "SELECT Capacity FROM Win32_PhysicalMemory", o => h.MemoryModules++);
            Query("root\\cimv2", "SELECT Name, DriverVersion FROM Win32_VideoController", o => {
                string name = Convert.ToString(o["Name"]);
                h.Gpus.Add(name + " | driver " + Convert.ToString(o["DriverVersion"]));
                h.DedicatedGpu |= LooksDedicated(name);
            });
            Query("root\\Microsoft\\Windows\\Storage", "SELECT FriendlyName, MediaType FROM MSFT_PhysicalDisk", o => {
                int media = Convert.ToInt32(o["MediaType"]);
                h.Disks.Add(Convert.ToString(o["FriendlyName"]) + (media == 4 ? " (SSD)" : media == 3 ? " (HDD)" : " (tipo não informado)"));
            });
            h.RefreshPower();
            return h;
        }

        public void RefreshPower()
        {
            PowerStatus p;
            PowerUnknown = !GetSystemPowerStatus(out p) || p.ACLineStatus == 255;
            OnBattery = !PowerUnknown && p.ACLineStatus == 0;
            Laptop |= p.BatteryFlag != 128 && p.BatteryFlag != 255;
        }

        internal static bool LooksDedicated(string name)
        {
            // Deliberately conservative. AdapterRAM (WMI uint32) is not reliable for modern VRAM.
            return Regex.IsMatch(name ?? "", @"GeForce|Quadro|RTX|Radeon.*\bRX\s*\d|Arc.*\b[AB]\d{3}\b", RegexOptions.IgnoreCase);
        }

        private static void Query(string scope, string query, Action<ManagementObject> visit)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(scope, query))
                {
                    s.Options.Timeout = TimeSpan.FromSeconds(5);
                    using (var rows = s.Get()) foreach (ManagementObject o in rows)
                        using (o) visit(o);
                }
            }
            catch (Exception e) { Log.W("Detecção parcial: " + e.Message); }
        }

        public void Show()
        {
            Ui.Title("FICHA TÉCNICA DETECTADA");
            Ui.Kv("Máquina", Machine);
            Ui.Kv("CPU", Cpu);
            Ui.Kv("Núcleos / processadores lógicos", Cores + " / " + Threads);
            Ui.Kv("RAM utilizável", Fmt.Gb(TotalRam));
            Ui.Kv("Módulos de RAM", MemoryModules + " (não comprova canais ativos)");
            foreach (var gpu in Gpus) Ui.Kv("GPU", gpu);
            Ui.Kv("Classe gráfica estimada", DedicatedGpu ? "dedicada detectada; confirmar GPU usada no jogo" : "integrada ou desconhecida; perfil conservador");
            Ui.Kv("Energia", PowerUnknown ? "não identificada" : OnBattery ? "bateria" : "tomada");
            foreach (var disk in Disks) Ui.Kv("Armazenamento", disk);
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                Ui.Kv("Espaço no disco do Windows", Fmt.Gb((ulong)drive.AvailableFreeSpace));
            }
            catch { }
        }
    }

    internal sealed class Calibration
    {
        public int MinCpu, BoostMode, Epp, HeapMb, PollSeconds;
        public ulong LowMemory, RecoverMemory;
        public Perfil Fifa;

        public static Calibration For(Hardware h, bool zomboid, bool mods)
        {
            double reserve = h.DedicatedGpu ? 4 : 5;
            double budget = Math.Min(h.RamGb * 0.5, h.RamGb - reserve);
            int heap = (int)(Math.Max(0, Math.Min(mods ? 12 : 8, budget)) * 1024) / 256 * 256;
            bool constrained = h.Laptop || h.OnBattery || h.PowerUnknown || !h.DedicatedGpu;
            ulong low = (ulong)(Math.Max(0.75, Math.Min(zomboid ? 3 : 2, h.RamGb * (zomboid ? 0.12 : 0.10))) * 1073741824);
            return new Calibration {
                MinCpu = constrained ? 5 : 20, BoostMode = constrained ? 1 : 2,
                Epp = constrained ? 33 : (zomboid ? 10 : 15), HeapMb = heap >= 2048 ? heap : 0,
                PollSeconds = zomboid ? 5 : 10, LowMemory = low, RecoverMemory = low + 536870912,
                Fifa = new Perfil {
                    Nome = "AUTOMÁTICO", Descricao = "Ponto de partida por hardware; compare a mesma partida antes e depois. Meta de FPS não é garantia.",
                    Largura = h.DedicatedGpu && h.RamGb >= 12 ? 1920 : 1280,
                    Altura = h.DedicatedGpu && h.RamGb >= 12 ? 1080 : 720,
                    MaxFps = h.DedicatedGpu && h.Threads >= 8 && h.RamGb >= 12 ? 60 : 30,
                    TargetFps = h.DedicatedGpu && h.Threads >= 8 && h.RamGb >= 12 ? 60 : 30,
                    EscalaRender = h.DedicatedGpu ? 1.0 : h.RamGb >= 12 ? 0.75 : 0.62
                }
            };
        }

        public void Show(bool zomboid)
        {
            Ui.Title("CALIBRAÇÃO PROPOSTA");
            Ui.Kv("CPU na tomada", "mínimo " + MinCpu + "%, máximo 100%, turbo " + BoostMode + ", EPP " + Epp);
            Ui.Kv("Reserva de RAM monitorada", Fmt.Gb(LowMemory));
            Ui.Kv("Intervalo de amostragem", PollSeconds + " s");
            if (zomboid) Ui.Kv("Limite do heap Java", HeapMb == 0 ? "RAM insuficiente para ajuste automático" : HeapMb + " MB (memória nativa fica fora deste limite)");
            else Ui.Kv("FC 26", Fifa.Largura + "x" + Fifa.Altura + ", escala " + Fifa.EscalaRender + ", teto " + Fifa.MaxFps + " FPS");
        }
    }
}
