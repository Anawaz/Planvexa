namespace Planvexa.Modules.Forms.Domain;

/// <summary>The supported form field input types.</summary>
public enum FormFieldType
{
    Text = 0,
    LongText = 1,
    Number = 2,
    Date = 3,
    Select = 4,

    /// <summary>Uploads via <see cref="Planvexa.BuildingBlocks.Abstractions.IFileStorage"/> —
    /// the submitted value is a <see cref="FormUpload"/> id issued by the pre-submission upload endpoint.</summary>
    FileUpload = 5,

    /// <summary>A true/false checkbox. Submitted value is "true"/"false".</summary>
    Boolean = 6,

    /// <summary>Basic email-shape validation (mirrors WorkManagement's Email custom field).</summary>
    Email = 7,

    /// <summary>Basic phone-shape validation (mirrors WorkManagement's Phone custom field).</summary>
    Phone = 8,

    /// <summary>Basic URL-shape validation (mirrors WorkManagement's Url custom field).</summary>
    Url = 9,
}

/// <summary>The comparison a field's visibility condition applies to the referenced field's value.</summary>
public enum FormFieldConditionOperator
{
    Equals = 0,
    NotEquals = 1,
    Contains = 2,
    IsEmpty = 3,
    IsNotEmpty = 4,
}
