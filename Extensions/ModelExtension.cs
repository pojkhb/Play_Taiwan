using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using backend.utils;

namespace backend.Extensions
{
    public static class ModelExtension
    {
        public static TDestination OneMatchAndMap<TSource, TDestination>(this TSource source, TDestination destination)
            where TSource : class, new()
            where TDestination : class, new()
        {
            if (source != null && destination != null)
            {
                List<PropertyInfo> sourceProperties = typeof(TSource).GetProperties().ToList<PropertyInfo>();
                List<PropertyInfo> destinationProperties = typeof(TDestination).GetProperties().ToList<PropertyInfo>();
                TDestination Result = new TDestination();

                Result = MappingItem<TSource, TDestination>(source, sourceProperties, destinationProperties);
                return Result;
            }
            return null;
        }

        public static List<TDestination> MatchAndMap<TSource, TDestination>(this List<TSource> source, List<TDestination> destination)
            where TSource : class, new()
            where TDestination : class, new()
        {
            if (source != null && destination != null)
            {
                List<PropertyInfo> sourceProperties = typeof(TSource).GetProperties().ToList<PropertyInfo>();
                List<PropertyInfo> destinationProperties = typeof(TDestination).GetProperties().ToList<PropertyInfo>();
                List<TDestination> Result = new List<TDestination>();

                foreach (var item in source)
                {
                    var data = MappingItem<TSource, TDestination>(item, sourceProperties, destinationProperties);
                    Result.Add(data);
                }
                return Result;
            }
            return null;
        }

        private static TDestination MappingItem<TSource, TDestination>(TSource source, IList<PropertyInfo> sourceProperties, IList<PropertyInfo> destinationProperties)
            where TSource : class, new()
            where TDestination : class, new()
        {
            TDestination Result = new TDestination();
            foreach (var sourceProperty in sourceProperties)
            {
                PropertyInfo destinationProperty = destinationProperties.Where(m => m.Name == sourceProperty.Name).FirstOrDefault();
                if (destinationProperty != null)
                {
                    if (sourceProperty.PropertyType == typeof(DateTime))
                    {
                        DateTime dt = new DateTime();
                        if (DateTime.TryParse(sourceProperty.GetValue(source, null).ToString(), out dt))
                        {
                            destinationProperty.SetValue(Result, common.DateFormat_full(dt), null);
                        }
                        else
                        {
                            destinationProperty.SetValue(Result, null, null);
                        }
                    }
                    if (sourceProperty.PropertyType == typeof(bool))
                    {
                        bool val = new bool();
                        if (Boolean.TryParse(sourceProperty.GetValue(source, null).ToString(), out val))
                        {
                            destinationProperty.SetValue(Result, val, null);
                        }
                        else
                        {
                            destinationProperty.SetValue(Result, null, null);
                        }
                    }
                    if (sourceProperty.PropertyType == typeof(decimal))
                    {
                        decimal val = new decimal();
                        decimal.TryParse(sourceProperty.GetValue(source, null).ToString(), out val);
                        destinationProperty.SetValue(Result, val.ToString(), null);
                    }
                    if (sourceProperty.PropertyType == typeof(double))
                    {
                        double val = new double();
                        double.TryParse(sourceProperty.GetValue(source, null).ToString(), out val);
                        destinationProperty.SetValue(Result, val.ToString(), null);
                    }
                    if (sourceProperty.PropertyType == typeof(int))
                    {
                        int val = new int();
                        int.TryParse(sourceProperty.GetValue(source, null).ToString(), out val);
                        destinationProperty.SetValue(Result, val.ToString(), null);
                    }
                    if (sourceProperty.PropertyType == typeof(Guid))
                    {
                        Guid val = new Guid();
                        Guid.TryParse(sourceProperty.GetValue(source, null).ToString(), out val);
                        destinationProperty.SetValue(Result, val.ToString(), null);
                    }
                    if (sourceProperty.GetValue(source, null) == null && sourceProperty.PropertyType == typeof(string))
                    {
                        destinationProperty.SetValue(Result, string.Empty, null);
                    }
                    if (sourceProperty.GetValue(source, null) != null && sourceProperty.PropertyType == typeof(string))
                    {
                        destinationProperty.SetValue(Result, sourceProperty.GetValue(source, null).ToString(), null);
                    }
                }
            }


            return Result;
        }
    }
}