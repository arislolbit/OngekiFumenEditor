using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OngekiFumenEditor.Base
{
    public abstract class GridBase : PropertyChangedBase, IComparable<GridBase>, ISerializable
    {
        private int grid = 0; //grid
        private float unit = 0; //unit

        protected uint gridBaseRadix = 2857;

        public GridBase(float unit = default, int grid = default)
        {
            this.grid = grid;
            this.unit = unit;
        }

        public int Grid
        {
            get
            {
                return grid;
            }
            set
            {
                grid = value;
                NotifyOfPropertyChange(() => Grid);
            }
        }
        public float Unit
        {
            get
            {
                return unit;
            }
            set
            {
                unit = value;
                NotifyOfPropertyChange(() => Unit);
            }
        }

        public int Compare(GridBase x, GridBase y)
        {
            return x.CompareTo(y);
        }

        public int CompareTo(GridBase other)
        {
            if (other.Unit != Unit)
            {
                return Math.Sign(Unit - other.Unit);
            }

            if (other.Grid != Grid)
            {
                return Grid - other.Grid;
            }

            return 0;
        }

        public static bool operator ==(GridBase l, GridBase r)
        {
            return l.CompareTo(r) == 0;
        }

        public static bool operator !=(GridBase l, GridBase r)
        {
            return !(l == r);
        }

        public static GridOffset operator -(GridBase l, GridBase r)
        {
            var unitDiff = l.Unit - r.Unit;
            long gridDiff = l.Grid - r.Grid;

            while (gridDiff < 0)
            {
                unitDiff = unitDiff - 1;
                gridDiff = gridDiff + l.gridBaseRadix;
            }

            return new GridOffset(unitDiff, (int)gridDiff);
        }

        public abstract string Serialize(OngekiFumen fumenData);
    }
}
