﻿using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using OpenSatelliteProject.Tools;
using System.Globalization;
using OpenSatelliteProject.PacketData.Enums;

namespace OpenSatelliteProject {
    public class Organizer {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private Dictionary<int, GroupData> groupData;
        private List<string> alreadyProcessed;

        private string folder;

        public Dictionary<int, GroupData> GroupData { get { return groupData; } }

        public Organizer(string folder) {
            this.folder = folder;    
            this.groupData = new Dictionary<int, GroupData>();
            this.alreadyProcessed = new List<string>();
        }

        public void Update() {
            List<string> files = Directory.GetFiles(folder).Where(f => f.EndsWith(".lrit")).ToList();
            foreach (string file in files) {
                if (alreadyProcessed.Contains(file)) {
                    continue;
                }
                try {
                    var header = FileParser.GetHeaderFromFile(file);
                    var anciliary = header.AncillaryHeader != null ? header.AncillaryHeader.Values : null;
                    var satellite = "Unknown";
                    var region = "Unknown";
                    var datetime = header.TimestampHeader.DateTime; // Defaults to capture time
                    var channel = 99;
                    var segmentId = header.SegmentIdentificationHeader != null ? header.SegmentIdentificationHeader.Sequence : 0;

                    if (header.Product.ID == (int)NOAAProductID.HIMAWARI8_ABI) {
                        channel = header.SubProduct.ID;
                        satellite = "HIMAWARI8";
                        region = "Full Disk";
                    }

                    if (anciliary != null) {
                        if (anciliary.ContainsKey("Satellite")) {
                            satellite = anciliary["Satellite"];
                        }

                        if (anciliary.ContainsKey("Region")) {
                            region = anciliary["Region"];
                        }

                        if (anciliary.ContainsKey("Channel")) {
                            channel = int.Parse(anciliary["Channel"]);
                        }

                        if (anciliary.ContainsKey("Time of frame start")) {
                            var dtstring = anciliary["Time of frame start"];
                            // 2017/055/05:45:18
                            // or
                            // 2017-03-27T15:45:38.2Z
                            if (dtstring[4] == '/') {
                                var year = dtstring.Substring(0, 4);
                                var dayOfYear = dtstring.Substring(5, 3);
                                var hours = dtstring.Substring(9, 2);
                                var minutes = dtstring.Substring(12, 2);
                                var seconds = dtstring.Substring(15, 2);
                                //Console.WriteLine("Year: {0}\nDay Of Year: {1}\nHours: {2}\nMinutes: {3}\nSeconds: {4}", year, dayOfYear, hours, minutes, seconds);
                                datetime = new DateTime(int.Parse(year), 1, 1, int.Parse(hours), int.Parse(minutes), int.Parse(seconds));
                                datetime = datetime.AddDays(int.Parse(dayOfYear));
                            } else {
                                datetime = DateTime.Parse(dtstring, null, DateTimeStyles.RoundtripKind);
                            }
                        } else {
                            Console.WriteLine("No Frame Time of Start found! Using capture time.");
                        }
                    }

                    var cropSection = region.ToLower().Contains("full disk") || header.IsFullDisk;
                    var timestamp = (int)Math.Floor((datetime - UnixEpoch).TotalSeconds);

                    if (timestamp < 0 && file.Contains("IMG_DK")) {
                        // Himawari-8 relay BUG
                        //IMG_DK01VIS_201704161550
                        string bfile = Path.GetFileName(file);
                        string hdt = bfile.Substring(12, 12);
                        var year = hdt.Substring(0, 4);
                        var month = hdt.Substring(4, 2);
                        var day = hdt.Substring(6, 2);
                        var hour = hdt.Substring(8, 2);
                        var minute = hdt.Substring(10, 2);
                        datetime = new DateTime(int.Parse(year), int.Parse(month), int.Parse(day), int.Parse(hour), int.Parse(minute), 0);
                        timestamp = (int)Math.Floor((datetime - UnixEpoch).TotalSeconds);
                    }

                    if (!groupData.ContainsKey(timestamp)) {
                        groupData[timestamp] = new GroupData();
                    }
                    var grp = groupData[timestamp];
                    grp.SatelliteName = satellite;
                    grp.RegionName = region;
                    grp.FrameTime = datetime;
                    grp.CropImage = cropSection;

                    var od = new OrganizerData();
                    switch (channel) {
                        case 1: // Visible
                            od = grp.Visible;
                            break;
                        case 2: // Visible for G16
                            if (satellite == "G16") {
                                od = grp.Visible;
                            } else {
                                Console.WriteLine("Unknown Channel {0}", channel);
                            }
                            break;
                        case 3: // Water Vapour
                            if (satellite == "HIMAWARI8") {
                                od = grp.Infrared;
                            } else {
                                od = grp.WaterVapour;
                            }
                            break;
                        case 4: // Infrared
                            od = grp.Infrared;
                            break;
                        case 7: 
                            if (satellite == "HIMAWARI8") {
                                od = grp.WaterVapour;
                            }
                            break;
                        case 8:
                            if (satellite == "G16") {
                                od = grp.WaterVapour;
                            }
                            break;
                        case 13: // Infrared for G16
                            if (satellite == "G16") {
                                od = grp.Infrared;
                            }
                            break;
                        default:
                            //Console.WriteLine("Unknown Channel {0}", channel);
                            continue;
                    } 


                    od.Segments[segmentId] = file;
                    if (od.Columns == -1) {
                        od.Columns = header.ImageStructureHeader.Columns;
                        od.Lines = header.ImageStructureHeader.Lines;
                        od.PixelAspect = header.ImageNavigationHeader.ColumnScalingFactor / (float)header.ImageNavigationHeader.LineScalingFactor;
                        od.ColumnOffset = header.ImageNavigationHeader.ColumnOffset;
                        if (header.SegmentIdentificationHeader != null) {
                            od.MaxSegments = header.SegmentIdentificationHeader.MaxSegments;
                        } else {
                            od.MaxSegments = 1;
                        }
                    } else {
                        od.Lines += header.ImageStructureHeader.Lines;
                    }
                    alreadyProcessed.Add(file);
                } catch (Exception e) {
                    Console.WriteLine("Error reading file {0}: {1}", file, e);
                    alreadyProcessed.Add(file);
                }
            }
            /*
            foreach (var i in groupData) {
                var data = i.Value;
                Console.WriteLine("Showing group({0}): ", i.Key);
                Console.WriteLine(data.ToString());
            }
            */
        }
    }
}

