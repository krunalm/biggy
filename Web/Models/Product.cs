using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Web.Models {
  public class Product {
    public String Sku { get; set; }
    public String Name { get; set; }
    public Decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }

    public Product() {
      this.CreatedAt = DateTime.Now;
    }

    public override bool Equals(object obj) {
      var p1 = (Product)obj;
      return this.Sku == p1.Sku;
    }

    // Consistent with Equals (keyed on Sku) for BiggyList's hash index (F4).
    public override int GetHashCode() {
      return this.Sku == null ? 0 : this.Sku.GetHashCode();
    }
  }
}