﻿using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using AvalonStudio.Commands;
using System;
using System.Collections.Generic;
using System.Composition;
using System.Drawing;
using System.Globalization;

namespace WalletWasabi.Gui.Converters
{
	public class PrivacyLevelValueConverter : IValueConverter
	{
        private readonly static Dictionary<string, DrawingGroup> Cache = new Dictionary<string, DrawingGroup>();

        public DrawingGroup GetIconByName(string icon)
        {
            if (!Cache.TryGetValue(icon, out var image))
            {
                if (Application.Current.Styles.TryGetResource(icon.ToString(), out object resource))
                {
                    image = resource as DrawingGroup;
                    Cache.Add(icon, image);
                }
                else
                {
                    throw new InvalidOperationException($"Icon {icon} not found");
                }
            }

            return image;
        }

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is int integer)
			{
				var shield = string.Empty;
				if (integer <= 1)
				{
					shield = "Critical";
				}
				else if (integer < 21)
				{
					shield = "Some";
				}
				else if (integer < 49)
				{
					shield = "Fine";
				}
				else
				{
					shield = "Strong";
				}
				return GetIconByName($"Privacy{shield}");
			}

			throw new InvalidOperationException();
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
}
