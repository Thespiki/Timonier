// WPF n'importe pas System.IO par défaut (conflit avec System.Windows.Shapes.Path) : on l'importe ici
// et on fixe Path sur System.IO.Path. Pour une forme géométrique, écrire System.Windows.Shapes.Path.
global using System.IO;
global using Path = System.IO.Path;
