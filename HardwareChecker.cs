using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;

namespace agilicomsptoolkit
{
    public class HardwareItem
    {
        public string ComponentType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public bool IsHealthy { get; set; } = true;
    }

    public static class HardwareChecker
    {
        public static async Task<List<HardwareItem>> RunDiagnosticsAsync()
        {
            var results = new List<HardwareItem>();

            await Task.Run(() =>
            {
                CheckProcessor(results);
                CheckMemory(results);
                CheckDiskDrives(results);
                CheckBattery(results);
            });

            return results;
        }

        private static void CheckProcessor(List<HardwareItem> results)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, LoadPercentage, NumberOfCores, MaxClockSpeed FROM Win32_Processor");
                using var collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        string name = obj["Name"]?.ToString() ?? "Unknown CPU";
                        string loadStr = obj["LoadPercentage"]?.ToString() ?? "0";
                        string cores = obj["NumberOfCores"]?.ToString() ?? "0";
                        string clock = obj["MaxClockSpeed"]?.ToString() ?? "0";

                        bool healthy = true;
                        string status = "Healthy";
                        if (int.TryParse(loadStr, out int load) && load > 95)
                        {
                            healthy = false;
                            status = "Critical Load";
                        }

                        results.Add(new HardwareItem
                        {
                            ComponentType = "Processor (CPU)",
                            Name = name,
                            Status = status,
                            Details = $"Cores: {cores} | Max Speed: {clock} MHz | Current Load: {loadStr}%",
                            IsHealthy = healthy
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add(new HardwareItem { ComponentType = "Processor (CPU)", Name = "Error", Status = "Error", Details = ex.Message, IsHealthy = false });
            }
        }

        private static void CheckMemory(List<HardwareItem> results)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, Manufacturer FROM Win32_PhysicalMemory");
                using var collection = searcher.Get();
                long totalBytes = 0;
                int moduleCount = 0;
                string speed = "";
                string mfg = "";

                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        if (long.TryParse(obj["Capacity"]?.ToString(), out long cap))
                        {
                            totalBytes += cap;
                        }
                        speed = obj["Speed"]?.ToString() ?? speed;
                        mfg = obj["Manufacturer"]?.ToString() ?? mfg;
                        moduleCount++;
                    }
                }

                if (moduleCount > 0)
                {
                    double gb = totalBytes / (1024.0 * 1024.0 * 1024.0);
                    results.Add(new HardwareItem
                    {
                        ComponentType = "Physical Memory (RAM)",
                        Name = $"{moduleCount} Module(s) ({mfg})",
                        Status = "Healthy",
                        Details = $"Total Capacity: {Math.Round(gb, 1)} GB | Speed: {speed} MHz",
                        IsHealthy = true
                    });
                }
                else
                {
                    results.Add(new HardwareItem
                    {
                        ComponentType = "Physical Memory (RAM)",
                        Name = "No Memory Detected",
                        Status = "Unknown",
                        Details = "No RAM modules reported by WMI.",
                        IsHealthy = false
                    });
                }
            }
            catch (Exception ex)
            {
                results.Add(new HardwareItem { ComponentType = "Memory (RAM)", Name = "Error", Status = "Error", Details = ex.Message, IsHealthy = false });
            }
        }

        private static void CheckDiskDrives(List<HardwareItem> results)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Model, Size, Status, MediaType FROM Win32_DiskDrive");
                using var collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        string model = obj["Model"]?.ToString() ?? "Unknown Drive";
                        string status = obj["Status"]?.ToString() ?? "Unknown";
                        string mediaType = obj["MediaType"]?.ToString() ?? "Unknown Media";
                        
                        double gb = 0;
                        if (long.TryParse(obj["Size"]?.ToString(), out long sizeBytes))
                        {
                            gb = sizeBytes / (1024.0 * 1024.0 * 1024.0);
                        }

                        bool healthy = status.Equals("OK", StringComparison.OrdinalIgnoreCase);
                        
                        results.Add(new HardwareItem
                        {
                            ComponentType = "Storage (Disk)",
                            Name = model,
                            Status = healthy ? "Healthy (SMART OK)" : $"Warning ({status})",
                            Details = $"Capacity: {Math.Round(gb, 1)} GB | Type: {mediaType}",
                            IsHealthy = healthy
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add(new HardwareItem { ComponentType = "Storage (Disk)", Name = "Error", Status = "Error", Details = ex.Message, IsHealthy = false });
            }
        }

        private static void CheckBattery(List<HardwareItem> results)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, EstimatedChargeRemaining, DesignCapacity, FullChargeCapacity, BatteryStatus FROM Win32_Battery");
                using var collection = searcher.Get();
                if (collection.Count == 0) return; // Desktop PC, no battery

                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        string name = obj["Name"]?.ToString() ?? "System Battery";
                        string chargeRemaining = obj["EstimatedChargeRemaining"]?.ToString() ?? "Unknown";
                        
                        double designCap = 0;
                        double fullCap = 0;
                        if (double.TryParse(obj["DesignCapacity"]?.ToString(), out double dc)) designCap = dc;
                        if (double.TryParse(obj["FullChargeCapacity"]?.ToString(), out double fc)) fullCap = fc;

                        string details = $"Current Charge: {chargeRemaining}%";
                        bool healthy = true;
                        string status = "Healthy";

                        if (designCap > 0 && fullCap > 0)
                        {
                            double healthPercent = (fullCap / designCap) * 100.0;
                            details += $" | Wear Level: {Math.Round(100.0 - healthPercent, 1)}% (Health: {Math.Round(healthPercent, 1)}%)";
                            
                            if (healthPercent < 50)
                            {
                                healthy = false;
                                status = "Degraded (Replace soon)";
                            }
                        }

                        results.Add(new HardwareItem
                        {
                            ComponentType = "Battery",
                            Name = name,
                            Status = status,
                            Details = details,
                            IsHealthy = healthy
                        });
                    }
                }
            }
            catch
            {
                // Ignore battery errors safely
            }
        }
    }
}
