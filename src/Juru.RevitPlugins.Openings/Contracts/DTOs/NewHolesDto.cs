using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Juru.RevitPlugins.Openings.Contracts.DTOs
{
    public class NewHolesDto : INotifyPropertyChanged
    {
        private bool _isInsert;

        public NewHolesDto(int id, PointDTO point, string intersectingElementType, string intersectingElementName, /*string description,*/ string holeType,
            double intersectingElementTypeSize, string sourceType, string shape, double holeOffset,
            bool isInsert, double sourceThickness, PointDTO intersectionNormal, double sourceWidth, string sourceName,
            double height, double width
            )
        {
            Id = id;
            IntersectionPoint = point;
            IntersectingElementType = intersectingElementType;
            IntersectingElementName = intersectingElementName;
            //Description = description;
            HoleType = holeType;
            IntersectingElementTypeSize = intersectingElementTypeSize;
            SourceType = sourceType;
            Shape = shape;
            HoleOffset = holeOffset;
            IsInsert = isInsert;
            SourceThickness = sourceThickness;
            IntersectionNormal = intersectionNormal;
            SourceWidth = sourceWidth;
            SourceName = sourceName;
            Height = height;
            Width = width;
        }

        public string IntersectingElementType { get; set; }

        public string SourceName { get; set; }
        public double Height { get; }
        public double Width { get; }

        public int Id { get; set; }
        public PointDTO IntersectionPoint { get; set; }
        public string IntersectingElementName { get; set; }
        public string Description { get; set; }
        public string HoleType { get; set; }
        public double IntersectingElementTypeSize { get; set; }
        public string SourceType { get; set; }
        public double SourceWidth { get; set; }
        public string Shape { get; set; }
        public double HoleOffset { get; set; }

        public bool IsInsert
        {
            get => _isInsert;
            set
            {
                _isInsert = value;
                OnPropertyChanged();
            }
        }
        public double SourceThickness { get; }
        public PointDTO IntersectionNormal { get; }
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
