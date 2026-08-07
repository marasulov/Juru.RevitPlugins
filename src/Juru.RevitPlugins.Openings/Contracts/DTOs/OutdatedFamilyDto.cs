using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace Juru.RevitPlugins.Openings.Contracts.DTOs
{
    public class OutdatedFamilyDto : INotifyPropertyChanged
    {
        private bool _isDelete;

        public OutdatedFamilyDto(string id, string name, PointDTO? location)
        {
            Id = id;
            Name = name;
            Location = location;
        }

        public string Id { get; }
        public bool IsDelete
        {
            get => _isDelete;
            set
            {
                _isDelete = value;
                OnPropertyChanged();
            }
        }
        public string Name { get; }
        public PointDTO? Location { get; }

        public bool Equals(EntityDTO? other)
        {
            var res = other is not null &&
                      Id.Equals(
                          other.Id,
                          StringComparison.InvariantCultureIgnoreCase);

            return res;
        }

        public override bool Equals(object? obj)
        {
            return
                obj is EntityDTO other
                && Equals(other);
        }

        public override int GetHashCode() => Id.GetHashCode();


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
