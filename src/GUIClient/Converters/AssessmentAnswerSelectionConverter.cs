using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using DAL.Entities;
using Microsoft.Extensions.Localization;

namespace GUIClient.Converters;

public class AssessmentAnswerSelectionConverter: IMultiValueConverter
{
    public static readonly AssessmentAnswerSelectionConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0)
        {
            return null;
        }
        
        if(values[0] is null || values[1] is null)
        {
            return null;
        }

        if (values[0]!.GetType() != typeof(int) || values[1]!.GetType() != typeof(List<AssessmentAnswer>))
        {
            return null;
        }
        

        if (targetType.IsAssignableTo(typeof(List<AssessmentAnswer>)))
        {
            var assessmentAnswers = (List<AssessmentAnswer>)values[1]!;
            var id = (int)values[0]!;
            var selectedAnswers = assessmentAnswers.Where(asw => asw.Id == id);
            return selectedAnswers;
        }

            
        // converter used for the wrong type
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }
    
 
}