#region Namespaces

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
#endregion

namespace RevitAddIn2BIMRU.Commands.AN
{
    [Transaction(TransactionMode.Manual)]
    public class WallSection : IExternalCommand
    {
      
        Document _doc;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            
            _doc = uiDoc.Document;

            Reference reference = uiDoc.Selection.PickObject(ObjectType.Element);
            Element element = uiDoc.Document.GetElement(reference);



            Wall wall = (Wall)element;



            ViewFamilyType vft = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault<ViewFamilyType>(x =>
                 ViewFamily.Section == x.ViewFamily);

            // Determine section box

            BoundingBoxXYZ sectionBox = GetSectionViewParallelToWall(wall);
            //BoundingBoxXYZ sectionBox2 = GetSectionViewPerpendiculatToWall(wall);

            // Create wall section view

            using (Transaction tx = new Transaction(_doc))
            {
                tx.Start("Create Wall Section View");

                ViewSection.CreateSection(_doc, vft.Id, sectionBox);
                //ViewSection.CreateSection(_doc, vft.Id, sectionBox2);

                tx.Commit();



            }


            return Result.Succeeded;
        }

        /// <summary>
        /// Return a section box for a view perpendicular 
        /// to the given wall location line.
        /// </summary>
        BoundingBoxXYZ GetSectionViewPerpendiculatToWall(Wall wall)
        {
            LocationCurve lc = wall.Location as LocationCurve;

            // Using 0.5 and "true" to specify that the 
            // parameter is normalized places the transform
            // origin at the center of the location curve

            Transform curveTransform = lc.Curve.ComputeDerivatives(0.5, true);

            // The transform contains the location curve
            // mid-point and tangent, and we can obtain
            // its normal in the XY plane:

            XYZ origin = curveTransform.Origin;
            XYZ viewdir = curveTransform.BasisX.Normalize();
            XYZ up = XYZ.BasisZ;
            XYZ right = up.CrossProduct(viewdir);

            // Set up view transform, assuming wall's "up" 
            // is vertical. For a non-vertical situation 
            // such as section through a sloped floor, the 
            // surface normal would be needed

            Transform transform = Transform.Identity;
            transform.Origin = origin;
            transform.BasisX = right;
            transform.BasisY = up;
            transform.BasisZ = viewdir;

            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
            sectionBox.Transform = transform;

            // Min & Max X values -10 and 10 define the 
            // section line length on each side of the wall.
            // Max Y (12) is the height of the section box.
            // Max Z (5) is the far clip offset.

            double d = wall.WallType.Width;
            BoundingBoxXYZ bb = wall.get_BoundingBox(null);

            double minZ = bb.Min.Z;
            double maxZ = bb.Max.Z;
            double h = maxZ - minZ;

            sectionBox.Min = new XYZ(-2 * d, -1, 0);
            sectionBox.Max = new XYZ(2 * d, h + 1, 5);

            return sectionBox;
        }

        /// <summary>
        /// Return a section box for a view parallel
        /// to the given wall location line.
        /// </summary>
        BoundingBoxXYZ GetSectionViewParallelToWall(Wall wall)
        {
            LocationCurve lc = wall.Location as LocationCurve;

            Curve curve = lc.Curve; //кривая стены

            // view direction sectionBox.Transform.BasisZ
            // up direction sectionBox.Transform.BasisY
            // right hand is computed so that (right, up, view direction) form a left handed coordinate system.  
            // crop region projections of BoundingBoxXYZ.Min and BoundingBoxXYZ.Max onto the view cut plane
            // far clip distance difference of the z-coordinates of BoundingBoxXYZ.Min and BoundingBoxXYZ.Max

            XYZ p = curve.GetEndPoint(0); //первая точка стены 
            XYZ q = curve.GetEndPoint(1); //вторая точка стены
            XYZ v = q - p; //длина стены точка

            BoundingBoxXYZ bb = wall.get_BoundingBox(null);
            double minZ = bb.Min.Z; //высота
            double maxZ = bb.Max.Z;

            double w = v.GetLength(); //длина стены
            double h = maxZ - minZ; //высота стены
            double d = wall.WallType.Width; //толщина стены

            double offset = d; //смещение разреза

            XYZ min = new XYZ(-w * 0.5 - 0.1, -0.1, -offset);
            //XYZ max = new XYZ( w, maxZ + offset, 0 ); // section view dotted line in center of wall
            XYZ max = new XYZ(w * 0.5 + 0.1, maxZ - minZ + 0.1, offset); // section view dotted line offset from center of wall

            XYZ midpoint = p + 0.5 * v;
            XYZ walldir = v.Normalize(); //Returns a new UV whose coordinates are the normalized values from this vector.
            XYZ up = XYZ.BasisZ;
            XYZ viewdir = walldir.CrossProduct(up);

            Transform t = Transform.Identity;
            t.Origin = midpoint;
            t.BasisX = walldir;
            t.BasisY = up;
            t.BasisZ = viewdir;

            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
            sectionBox.Transform = t;
            sectionBox.Min = min;
            sectionBox.Max = max;

            return sectionBox;
        }


    }
}
