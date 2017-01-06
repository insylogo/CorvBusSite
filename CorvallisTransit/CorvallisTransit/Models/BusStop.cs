using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CorvallisTransit.Models
{
    public class BusStop : IEqualityComparer<BusStop>
    {
        public string StopTag { get; set; }

        public string Address { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string StopNumber { get; set; }

        public BusStop()
        {

        }

        public override bool Equals(object obj)
        {
            return (obj as BusStop) != null ? (obj as BusStop).GetHashCode() == GetHashCode() : false;
        }

        public override int GetHashCode()
        {
            return (Address+StopTag).GetHashCode();
        }

        public bool Equals(BusStop x, BusStop y)
        {
            return x.Equals(y);
        }

        public int GetHashCode(BusStop obj)
        {
            return obj.GetHashCode();
        }
    }
}
