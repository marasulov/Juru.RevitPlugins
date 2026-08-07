using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Juru.RevitPlugins.Openings.Contracts.DTOs
{
    public class ActualHoleModelDto : INotifyPropertyChanged, IHoleModel

    {
        private bool _isDelete;

        public ActualHoleModelDto(int id, PointDTO point, string intersectingElementName, string intersectingElementType,
            string holeType,
            double intersectingElementTypeSize, string sourceType, bool isHoleRectangled, double holeOffset,
            bool isDelete, double sourceThickness, PointDTO intersectionNormal, double sourceWidth, string sourceName)
        {
            Id = id;
            IntersectionPoint = point;
            IntersectingElementName = intersectingElementName;
            IntersectingElementType = intersectingElementType;
            HoleType = holeType;
            IntersectingElementTypeSize = intersectingElementTypeSize;
            SourceType = sourceType;
            IsHoleRectangled = isHoleRectangled;
            HoleOffset = holeOffset;
            IsDelete = isDelete;
            SourceThickness = sourceThickness;
            IntersectionNormal = intersectionNormal;
            SourceWidth = sourceWidth;
            SourceName = sourceName;
        }

        public int Id { get; set; }
        public PointDTO IntersectionPoint { get; set; }
        public string IntersectingElementName { get; set; }
        public string IntersectingElementType { get; set; }
        public string HoleType { get; set; }
        public double IntersectingElementTypeSize { get; set; }
        public string SourceType { get; set; }
        public double SourceWidth { get; set; }
        public bool IsHoleRectangled { get; set; }
        public double HoleOffset { get; set; }

        public bool IsDelete
        {
            get => _isDelete;
            set
            {
                _isDelete = value;
                OnPropertyChanged();
            }
        }

        public double SourceThickness { get; }
        public PointDTO IntersectionNormal { get; }
        public string SourceName { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
