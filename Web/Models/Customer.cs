using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Web.Models {
  public class Customer {

    public string Email { get; set; }
    public string First { get; set; }
    public string Last { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string FullName {
      get {
        return this.First + " " + this.Last;
      }
    }

    public override bool Equals(object obj) {
      var c1 = (Customer)obj;
      return c1.Email == this.Email;
    }

    // Consistent with Equals (keyed on Email) for BiggyList's hash index (F4).
    public override int GetHashCode() {
      return this.Email == null ? 0 : this.Email.GetHashCode();
    }

    public override string ToString() {
      return this.FullName;
    }
  }
}