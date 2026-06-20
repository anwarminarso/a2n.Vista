using System;
using System.Collections.Generic;

namespace a2n.Vista.Examples.Northwind.Entities;

/// <summary>
/// An employee of the company. Maps to the Northwind <c>Employees</c> table; self-referencing via
/// <see cref="ReportsTo"/> / <see cref="ReportsToNavigation"/>.
/// </summary>
public partial class Employee
{
    public int EmployeeId { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string TitleOfCourtesy { get; set; } = string.Empty;

    public DateOnly? BirthDate { get; set; }

    public DateOnly? HireDate { get; set; }

    public string Address { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string HomePhone { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;

    public byte[]? Photo { get; set; }

    public string Notes { get; set; } = string.Empty;

    public int? ReportsTo { get; set; }

    public string PhotoPath { get; set; } = string.Empty;

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<Employee> InverseReportsToNavigation { get; set; } = new List<Employee>();

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual Employee ReportsToNavigation { get; set; } = null!;

    // [Newtonsoft.Json.JsonIgnore]
    // [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<Territory> Territories { get; set; } = new List<Territory>();
}
