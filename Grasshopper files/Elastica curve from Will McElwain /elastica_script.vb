Option Strict Off
Option Explicit On

Imports Rhino
Imports Rhino.Geometry
Imports Rhino.DocObjects
Imports Rhino.Collections

Imports GH_IO
Imports GH_IO.Serialization
Imports Grasshopper
Imports Grasshopper.Kernel
Imports Grasshopper.Kernel.Data
Imports Grasshopper.Kernel.Types

Imports System
Imports System.IO
Imports System.Xml
Imports System.Xml.Linq
Imports System.Linq
Imports System.Data
Imports System.Drawing
Imports System.Reflection
Imports System.Collections
Imports System.Windows.Forms
Imports Microsoft.VisualBasic
Imports System.Collections.Generic
Imports System.Runtime.InteropServices



''' <summary>
''' This class will be instantiated on demand by the Script component.
''' </summary>
Public Class Script_Instance
  Inherits GH_ScriptInstance

  #Region "Utility functions"
  ''' <summary>Print a String to the [Out] Parameter of the Script component.</summary>
  ''' <param name="text">String to print.</param>
  Private Sub Print(ByVal text As String)
    'Implementation hidden in Script Edit mode...
  End Sub
  ''' <summary>Print a formatted String to the [Out] Parameter of the Script component.</summary>
  ''' <param name="format">String format.</param>
  ''' <param name="args">Formatting parameters.</param>
  Private Sub Print(ByVal format As String, ByVal ParamArray args As Object())
    'Implementation hidden in Script Edit mode...
  End Sub
  ''' <summary>Print useful information about an object instance to the [Out] Parameter of the Script component. </summary>
  ''' <param name="obj">Object instance to parse.</param>
  Private Sub Reflect(ByVal obj As Object)
    'Implementation hidden in Script Edit mode...
  End Sub
  ''' <summary>Print the signatures of all the overloads of a specific method to the [Out] Parameter of the Script component. </summary>
  ''' <param name="obj">Object instance to parse.</param>
  Private Sub Reflect(ByVal obj As Object, ByVal method_name As String)
    'Implementation hidden in Script Edit mode...
  End Sub
#End Region

#Region "Members"
  ''' <summary>Gets the current Rhino document.</summary>
  Private Readonly RhinoDocument As RhinoDoc
  ''' <summary>Gets the Grasshopper document that owns this script.</summary>
  Private Readonly GrasshopperDocument as GH_Document
  ''' <summary>Gets the Grasshopper script component that owns this script.</summary>
  Private Readonly Component As IGH_Component
  ''' <summary>
  ''' Gets the current iteration count. The first call to RunScript() is associated with Iteration=0.
  ''' Any subsequent call within the same solution will increment the Iteration count.
  ''' </summary>
  Private Readonly Iteration As Integer
#End Region

  ''' <summary>
  ''' This procedure contains the user code. Input parameters are provided as ByVal arguments,
  ''' Output parameter are ByRef arguments. You don't have to assign output parameters,
  ''' they will have default values.
  ''' </summary>
  Private Sub RunScript(ByVal PtA As Point3d, ByVal PtB As Point3d, ByVal Pln As Plane, ByVal Len As Double, ByVal Wid As Double, ByVal Ht As Double, ByVal Ang As Double, ByVal E As Double, ByVal I As Double, ByRef Pts As Object, ByRef Crv As Object, ByRef L As Object, ByRef W As Object, ByRef H As Object, ByRef A As Object, ByRef F As Object) 
    ' -----------------------------------------------------------------
    ' Elastic Bending Script by Will McElwain
    ' Created February 2014
    '
    ' DESCRIPTION:
    ' This beast creates the so-called 'elastica curve', the shape a long, thin rod or wire makes when it is bent elastically (i.e. not permanently). In this case, force
    ' is assumed to only be applied horizontally (which would be in line with the rod at rest) and both ends are assumed to be pinned or hinged meaning they are free
    ' to rotate (as opposed to clamped, when the end tangent angle is fixed, usually horizontally). An interesting finding is that it doesn't matter what the material or
    ' cross-sectional area is, as long as they're uniform along the entire length. Everything makes the same shape when bent as long as it doesn't cross the threshold
    ' from elastic to plastic (permanent) deformation (I don't bother to find that limit here, but can be found if the yield stress for a material is known).
    '
    ' Key to the formulas used in this script are elliptic integrals, specifically K(m), the complete elliptic integral of the first kind, and E(m), the complete elliptic
    ' integral of the second kind. There was a lot of confusion over the 'm' and 'k' parameters for these functions, as some people use them interchangeably, but they are
    ' not the same. m = k^2 (thus k = Sqrt(m)). I try to use the 'm' parameter exclusively to avoid this confusion. Note that there is a unique 'm' parameter for every
    ' configuration/shape of the elastica curve.
    '
    ' This script tries to find that unique 'm' parameter based on the inputs. The algorithm starts with a test version of m, evaluates an expression, say 2*E(m)/K(m)-1,
    ' then compares the result to what it should be (in this case, a known width/length ratio). Iterate until the correct m is found. Once we have m, we can then calculate
    ' all of the other unknowns, then find points that lie on that curve, then interpolate those points for the actual curve. You can also use Wolfram|Alpha as I did to
    ' find the m parameter based on the equations in this script (example here: http://tiny.cc/t4tpbx for when say width=45.2 and length=67.1).
    '
    ' Other notes:
    ' * This script works with negative values for width, which will creat a self-intersecting curve (as it should). The curvature of the elastica starts to break down around
    ' m=0.95 (~154°), but this script will continue to work until M_MAX, m=0.993 (~169°). If you wish to ignore self-intersecting curves, set ignoreSelfIntersecting to True
    ' * When the only known values are length and height, it is actually possible for certain ratios of height to length to have two valid m values (thus 2 possible widths
    ' and angles). This script will return them both.
    ' * Only the first two valid parameters (of the required ones) will be used, meaning if all four are connected (length, width or a PtB, height, and angle), this script will
    ' only use length and width (or a PtB).
    ' * Depending on the magnitude of your inputs (say if they're really small, like if length < 10), you might have to increase the constant ROUNDTO at the bottom
    '
    ' REFERENCES:
    ' {1} "The elastic rod" by M.E. Pacheco Q. & E. Pina, http://www.scielo.org.mx/pdf/rmfe/v53n2/v53n2a8.pdf
    ' {2} "An experiment in nonlinear beam theory" by A. Valiente, http://www.deepdyve.com/lp/doc/I3lwnxdfGz , also here: http://tiny.cc/Valiente_AEiNBT
    ' {3} "Snap buckling, writhing and Loop formation In twisted rods" by V.G.A. GOSS, http://myweb.lsbu.ac.uk/~gossga/thesisFinal.pdf
    ' {4} "Theory of Elastic Stability" by Stephen Timoshenko, http://www.scribd.com/doc/50402462/Timoshenko-Theory-of-Elastic-Stability  (start on p. 76)
    '
    ' INPUT:
    ' PtA - First anchor point (required)
    ' PtB - Second anchor point (optional, though 2 out of the 4--length, width, height, angle--need to be specified)
    '       [note that PtB can be the same as PtA (meaning width would be zero)]
    '       [also note that if a different width is additionally specified that's not equal to the distance between PtA and PtB, then the end point will not equal PtB anymore]
    ' Pln - Plane of the bent rod/wire, which bends up in the +y direction. The line between PtA and PtB (if specified) must be parallel to the x-axis of this plane
    '
    ' ** 2 of the following 4 need to be specified **
    ' Len - Length of the rod/wire, which needs to be > 0
    ' Wid - Width between the endpoints of the curve [note: if PtB is specified in addition, and distance between PtA and PtB <> width, the end point will be relocated
    ' Ht - Height of the bent rod/wire (when negative, curve will bend downward, relative to the input plane, instead)
    ' Ang - Inner departure angle or tangent angle (in radians) at the ends of the bent rod/wire. Set up so as width approaches length (thus height approaches zero), angle approaches zero
    '
    ' * Following variables only needed for optional calculating of bending force, not for shape of curve.
    ' E - Young's modulus (modulus of elasticity) in GPa (=N/m^2) (material-specific. for example, 7075 aluminum is roughly 71.7 GPa)
    ' I - Second moment of area (or area moment of inertia) in m^4 (cross-section-specific. for example, a hollow rod
    '     would have I = pi * (outer_diameter^4 - inner_diameter^4) / 32
    ' Note: E*I is also known as flexural rigidity or bending stiffness
    '
    ' OUTPUT:
    ' out - only for debugging messages
    ' Pts - the list of points that approximate the shape of the elastica
    ' Crv - the 3rd-degree curve interpolated from those points (with accurate start & end tangents)
    ' L - the length of the rod/wire
    ' W - the distance (width) between the endpoints of the rod/wire
    ' H - the height of the bent rod/wire
    ' A - the tangent angle at the (start) end of the rod/wire
    ' F - the force needed to hold the rod/wire in a specific shape (based on the material properties & cross-section) **be sure your units for 'I' match your units for the
    ' rest of your inputs (length, width, etc.). Also note that the critical buckling load (force) that makes the rod/wire start to bend can be found at height=0
    '
    ' THANKS TO:
    ' Mårten Nettelbladt (thegeometryofbending.blogspot.com)
    ' Daniel Piker (Kangaroo plugin)
    ' David Rutten (Grasshopper guru)
    ' Euler & Bernoulli (the O.G.'s)
    '
    ' -----------------------------------------------------------------

    Dim ignoreSelfIntersecting As Boolean = False  ' set to True if you don't want to output curves where width < 0, which creates a self-intersecting curve

    Dim inCt As Integer = 0  ' count the number of required parameters that are receiving data
    Dim length As Double
    Dim width As System.Object = Nothing  ' need to set as Nothing so we can check if it has been assigned a value later
    Dim height As Double
    Dim angle As Double
    Dim m As Double
    Dim multiple_m As New List(Of Double)
    Dim AtoB As Line
    Dim flip_H As Boolean = False  ' if height is negative, this flag will be set
    Dim flip_A As Boolean = False  ' if angle is negative, this flag will be set

    If Not IsSet("Pln") Then
      Msg("error", "Base plane is not set")
      Return
    End If

    If Not IsSet("PtA") Then
      Msg("error", "Point A is not set")
      Return
    End If

    If Math.Round(Pln.DistanceTo(PtA), Defined.ROUNDTO) <> 0 Then
      Msg("error", "Point A is not on the base plane")
      Return
    End If

    Dim refPlane As Plane = Pln  ' create a reference plane = input plane and set the origin of it to PtA in case PtA isn't the origin already
    refPlane.Origin = PtA

    If IsSet("PtB") Then
      If Math.Round(Pln.DistanceTo(PtB), Defined.ROUNDTO) <> 0 Then
        Msg("error", "Point B is not on the base plane")
        Return
      End If

      AtoB = New Line(PtA, PtB)
      If AtoB.Length <> 0 And Not AtoB.Direction.IsPerpendicularTo(Pln.YAxis) Then
        Msg("error", "The line between PtA and PtB is not perpendicular to the Y-axis of the specified plane")
        Return
      End If

      inCt += 1
      If IsSet("Wid") Then Msg("info", "Wid will override the distance between PtA and PtB. If you do not want this to happen, disconnect PtB or Wid.")

      width = PtA.DistanceTo(PtB)  ' get the width (distance) between PtA and PtB

      Dim refPtB As Point3d
      refPlane.RemapToPlaneSpace(PtB, refPtB)
      If refPtB.X < 0 Then width = -width  ' check if PtB is to the left of PtA...if so, width is negative
    End If

    If IsSet("Len") Then inCt += 1
    If IsSet("Wid") Then inCt += 1
    If IsSet("Ht") Then inCt += 1
    If IsSet("Ang") Then inCt += 1
    If inCt > 2 Then Msg("info", "More parameters set than are required (out of length, width, height, angle). Only using the first two valid ones.")

    ' check for connected/specified inputs. note: only the first two that it comes across will be used
    If IsSet("Len") Then  ' if length is specified then...
      If Len <= 0 Then
        Msg("error", "Length cannot be negative or zero")
        Return
      End If
      If IsSet("Wid") Then  ' find height & angle based on length and specified width
        If Wid > Len Then
          Msg("error", "Width is greater than length")
          Return
        End If
        If Wid = Len Then  ' skip the solver and set the known values
          height = 0
          m = 0
          angle = 0
          width = Wid
        Else
          m = SolveMFromLenWid(Len, Wid)
          height = Cal_H(Len, m)  ' L * Sqrt(m) / K(m)
          angle = Cal_A(m)  ' Acos(1 - 2 * m)
          width = Wid
        End If

      Else If width IsNot Nothing Then  ' find height & angle based on length and calculated width (distance between PtA and PtB)
        If width > Len Then
          Msg("error", "Width is greater than length")
          Return
        End If
        If width = Len Then  ' skip the solver and set the known values
          height = 0
          m = 0
          angle = 0
        Else
          m = SolveMFromLenWid(Len, width)
          height = Cal_H(Len, m)  ' L * Sqrt(m) / K(m)
          angle = Cal_A(m)  ' Acos(1 - 2 * m)
        End If

      Else If IsSet("Ht") Then  ' find width & angle based on length and height  ** possible to return 2 results **
        If Math.Abs(Ht / Len) > Defined.MAX_HL_RATIO Then
          Msg("error", "Height not possible with given length")
          Return
        End If
        If Ht < 0 Then
          Ht = -Ht  ' if height is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
          refPlane.Transform(Transform.Mirror(New Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)))
          flip_A = True
          flip_H = True
        End If
        If Ht = 0 Then  ' skip the solver and set the known values
          width = Len
          angle = 0
        Else
          multiple_m = SolveMFromLenHt(Len, Ht)  ' note that it's possible for two values of m to be found if height is close to max height
          If multiple_m.Count = 1 Then  ' if there's only one m value returned, calculate the width & angle here. we'll deal with multiple m values later
            m = multiple_m.Item(0)
            width = Cal_W(Len, m)  ' L * (2 * E(m) / K(m) - 1)
            angle = Cal_A(m)  ' Acos(1 - 2 * m)
          End If
        End If
        height = Ht

      Else If IsSet("Ang") Then  ' find width & height based on length and angle
        If Ang < 0 Then
          Ang = -Ang  ' if angle is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
          refPlane.Transform(Transform.Mirror(New Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)))
          flip_A = True
          flip_H = True
        End If
        m = Cal_M(Ang)  ' (1 - Cos(a)) / 2
        If Ang = 0 Then  ' skip the solver and set the known values
          width = Len
          height = 0
        Else
          width = Cal_W(Len, m)  ' L * (2 * E(m) / K(m) - 1)
          height = Cal_H(Len, m)  ' L * Sqrt(m) / K(m)
        End If
        angle = Ang

      Else
        Msg("error", "Need to specify one more parameter in addition to length")
        Return
      End If
      length = Len

    Else If IsSet("Wid") Then  ' if width is specified then...
      If IsSet("Ht") Then  ' find length & angle based on specified width and height
        If Ht < 0 Then
          Ht = -Ht  ' if height is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
          refPlane.Transform(Transform.Mirror(New Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)))
          flip_A = True
          flip_H = True
        End If
        If Ht = 0 Then  ' skip the solver and set the known values
          length = Wid
          angle = 0
        Else
          m = SolveMFromWidHt(Wid, Ht)
          length = Cal_L(Ht, m)  ' h * K(m) / Sqrt(m)
          angle = Cal_A(m)  ' Acos(1 - 2 * m)
        End If
        height = Ht

      Else If IsSet("Ang") Then  ' find length & height based on specified width and angle
        If Wid = 0 Then
          Msg("error", "Curve not possible with width = 0 and an angle as inputs")
          Return
        End If
        If Ang < 0 Then
          Ang = -Ang  ' if angle is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
          refPlane.Transform(Transform.Mirror(New Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)))
          flip_A = True
          flip_H = True
        End If
        m = Cal_M(Ang)  ' (1 - Cos(a)) / 2
        If Ang = 0 Then  ' skip the solver and set the known values
          length = Wid
          height = 0
        Else
          length = Wid / (2 * EllipticE(m) / EllipticK(m) - 1)
          If length < 0 Then
            Msg("error", "Curve not possible at specified width and angle (calculated length is negative)")
            Return
          End If
          height = Cal_H(length, m)  ' L * Sqrt(m) / K(m)
        End If
        angle = Ang

      Else
        Msg("error", "Need to specify one more parameter in addition to width (Wid)")
        Return
      End If
      width = Wid

    Else If width IsNot Nothing Then  ' if width is determined by PtA and PtB then...
      If IsSet("Ht") Then  ' find length & angle based on calculated width and height
        If Ht < 0 Then
          Ht = -Ht  ' if height is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
          refPlane.Transform(Transform.Mirror(New Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)))
          flip_A = True
          flip_H = True
        End If
        If Ht = 0 Then  ' skip the solver and set the known values
          length = width
          angle = 0
        Else
          m = SolveMFromWidHt(width, Ht)
          length = Cal_L(Ht, m)  ' h * K(m) / Sqrt(m)
          angle = Cal_A(m)  ' Acos(1 - 2 * m)
        End If
        height = Ht

      Else If IsSet("Ang") Then  ' find length & height based on calculated width and angle
        If width = 0 Then
          Msg("error", "Curve not possible with width = 0 and an angle as inputs")
          Return
        End If
        If Ang < 0 Then
          Ang = -Ang  ' if angle is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
          refPlane.Transform(Transform.Mirror(New Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)))
          flip_A = True
          flip_H = True
        End If
        m = Cal_M(Ang)  ' (1 - Cos(a)) / 2
        If Ang = 0 Then  ' skip the solver and set the known values
          length = width
          height = 0
        Else
          length = width / (2 * EllipticE(m) / EllipticK(m) - 1)
          If length < 0 Then
            Msg("error", "Curve not possible at specified width and angle (calculated length is negative)")
            Return
          End If
          height = Cal_H(length, m)  ' L * Sqrt(m) / K(m)
        End If
        angle = Ang

      Else
        Msg("error", "Need to specify one more parameter in addition to PtA and PtB")
        Return
      End If

    Else If IsSet("Ht") Then  ' if height is specified then...
      If IsSet("Ang") Then  ' find length & width based on height and angle
        If Ht < 0 Then
          Ht = -Ht  ' if height is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
          refPlane.Transform(Transform.Mirror(New Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)))
          flip_H = True
          flip_A = True
        End If
        If Ht = 0 Then
          Msg("error", "Height can't = 0 if only height and angle are specified")
          Return
        Else
          If Ang < 0 Then
            Ang = -Ang  ' if angle is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
            refPlane.Transform(Transform.Mirror(New Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)))
            flip_A = Not flip_A
            flip_H = Not flip_H
          End If
          m = Cal_M(Ang)  ' (1 - Cos(a)) / 2
          If Ang = 0 Then
            Msg("error", "Angle can't = 0 if only height and angle are specified")
            Return
          Else
            length = Cal_L(Ht, m)  ' h * K(m) / Sqrt(m)
            width = Cal_W(length, m)  ' L * (2 * E(m) / K(m) - 1)
          End If
          angle = Ang
        End If
        height = Ht

      Else
        Msg("error", "Need to specify one more parameter in addition to height")
        Return
      End If

    Else If IsSet("Ang") Then
      Msg("error", "Need to specify one more parameter in addition to angle")
      Return
    Else
      Msg("error", "Need to specify two of the four parameters: length, width (or PtB), height, and angle")
      Return
    End If

    If m > Defined.M_MAX Then
      Msg("error", "Form of curve not solvable with current algorithm and given inputs")
      Return
    End If

    refPlane.Origin = refPlane.PointAt(width / 2, 0, 0)  ' adjust the origin of the reference plane so that the curve is centered about the y-axis (start of the curve is at x = -width/2)

    If multiple_m.Count > 1 Then  ' if there is more than one m value returned, calculate the width, angle, and curve for each
      Dim multi_pts As New DataTree(Of Point3d)
      Dim multi_crv As New List(Of Curve)
      Dim tmp_pts As New List(Of Point3d)
      Dim multi_W, multi_A, multi_F As New List(Of Double)
      Dim j As Integer = 0  ' used for creating a new branch (GH_Path) for storing pts which is itself a list of points

      For Each m_val As Double In multiple_m
        width = Cal_W(length, m_val) 'length * (2 * EllipticE(m_val) / EllipticK(m_val) - 1)

        If width < 0 And ignoreSelfIntersecting Then
          Msg("warning", "One curve is self-intersecting. To enable these, set ignoreSelfIntersecting to False")
          Continue For
        End If

        If m_val >= Defined.M_SKETCHY Then Msg("info", "Accuracy of the curve whose width = " & Math.Round(width, 4) & " is not guaranteed")

        angle = Cal_A(m_val) 'Math.Asin(2 * m_val - 1)
        refPlane.Origin = refPlane.PointAt(width / 2, 0, 0)  ' adjust the origin of the reference plane so that the curve is centered about the y-axis (start of the curve is at x = -width/2)

        tmp_pts = FindBendForm(length, width, m_val, angle, refPlane)
        multi_pts.AddRange(tmp_pts, New GH_Path(j))
        multi_crv.Add(MakeCurve(tmp_pts, angle, refPlane))

        multi_W.Add(width)
        If flip_A Then angle = -angle
        multi_A.Add(angle)

        E = E * 10 ^ 9  ' Young's modulus input E is in GPa, so we convert to Pa here (= N/m^2)
        multi_F.Add(EllipticK(m_val) ^ 2 * E * I / length ^ 2)  ' from reference {4} pg. 79

        j += 1
        refPlane.Origin = PtA  ' reset the reference plane origin to PtA for the next m_val
        'Print("length=" & length & ", width=" & width & ", height=" & height & ", angle=" & angle & ", m=" & m_val & ", k=" & Math.Sqrt(m_val) & ", w/L=" & width / length & ", h/L=" & height / length & ", w/h=" & width / height)
      Next

      ' assign the outputs
      Pts = multi_pts
      Crv = multi_crv
      L = length
      W = multi_W
      If flip_H Then height = -height
      H = height
      A = multi_A
      F = multi_F

    Else  ' only deal with the single m value
      If m >= Defined.M_SKETCHY Then Msg("info", "Accuracy of the curve at these parameters is not guaranteed")

      If width < 0 And ignoreSelfIntersecting Then
        Msg("error", "Curve is self-intersecting. To enable these, set ignoreSelfIntersecting to False")
        Return
      End If

      Pts = FindBendForm(length, width, m, angle, refPlane)
      Crv = MakeCurve(pts, angle, refPlane)
      L = length
      W = width
      If flip_H Then height = -height
      H = height
      If flip_A Then angle = -angle
      A = angle

      E = E * 10 ^ 9  ' Young's modulus input E is in GPa, so we convert to Pa here (= N/m^2)
      F = EllipticK(m) ^ 2 * E * I / length ^ 2  ' from reference {4} pg. 79. Note: the critical buckling (that makes the rod/wire start to bend) can be found at height=0 (width=length)

      'height = Math.Sqrt(((2 * Len / 5) ^ 2 - ((Wid - Len / 5) / 2) ^ 2)  ' quick approximation discovered by Mårten of 'Geometry of Bending' fame ( http://tiny.cc/it2pbx )
      'width = (Len +/- 2 * Math.Sqrt(4 * Len ^ 2 - 25 * Ht ^ 2)) / 5  ' derived from above
      'length = (2 * Math.Sqrt(15 * Ht ^ 2 + 4 * Wid ^ 2) - Wid) / 3  ' derived from above

      'Print("length=" & length & ", width=" & width & ", height=" & height & ", angle=" & angle & ", m=" & m & ", k=" & Math.Sqrt(m) & ", w/L=" & width / length & ", h/L=" & height / length & ", w/h=" & width / height)
    End If

  End Sub 

  '<Custom additional code> 
  Private Function IsSet(ByVal param As String) As Boolean  ' Check if an input parameter has data
    Dim i As Integer = Component.Params.IndexOfInputParam(param)
    If i > -1 Then
      Return Component.Params.Input.ElementAt(i).DataType > 1  ' input parameter DataType of 1 means it's not receiving input (internal or external)
    Else
      Msg("error", "Input parameter '" & param & "' not found")
      Return False
    End If
  End Function

  Private Sub Msg(ByVal type As String, ByVal msg As String)  ' Output an error, warning, or informational message
    Select Case type
      Case "error"
        Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, msg)
        Print("Error: " & msg)
      Case "warning"
        Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, msg)
        Print("Warning: " & msg)
      Case "info"
        Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, msg)
        Print(msg)
    End Select
  End Sub

  ' Solve for the m parameter from length and width (reference {1} equation (34), except b = width and K(k) and E(k) should be K(m) and E(m))
  Private Function SolveMFromLenWid(ByVal L As Double, ByVal w As Double) As Double
    If w = 0 Then
      Return Defined.M_ZERO_W  ' for the boundry condition width = 0, bypass the function and return the known m value
    End If

    Dim n As Integer = 1 ' Iteration counter (quit if >MAXIT)
    Dim lower As Double = 0 ' m must be within this range
    Dim upper As Double = 1
    Dim m As Double
    Dim cwl As Double

    Do While (upper - lower) > Defined.MAXERR AndAlso (n) < Defined.MAXIT ' Repeat until range narrow enough or MAXIT
      m = (upper + lower) / 2
      cwl = 2 * EllipticE(m) / EllipticK(m) - 1  ' calculate w/L with the test value of m
      If cwl < w / L Then  ' compares the calculated w/L with the actual w/L then narrows the range of possible m
        upper = m
      Else
        lower = m
      End If
      n += 1
    Loop
    Return m
  End Function

  ' Solve for the m parameter from length and height (reference {1} equation (33), except K(k) should be K(m) and k = sqrt(m))
  ' Note that it's actually possible to find 2 valid values for m (hence 2 width values) at certain height values
  Private Function SolveMFromLenHt(ByVal L As Double, ByVal h As Double) As List(Of Double)
    Dim n As Integer = 1 ' Iteration counter (quit if >MAXIT)
    Dim lower As Double = 0 ' m must be within this range
    Dim upper As Double = 1
    Dim twoWidths As Boolean = h / L >= Defined.DOUBLE_W_HL_RATIO And h / L < Defined.MAX_HL_RATIO  ' check to see if h/L is within the range where 2 solutions for the width are possible
    Dim m As Double
    Dim mult_m As New List(Of Double)
    Dim chl As Double

    If twoWidths Then
      ' find the first of two possible solutions for m with the following limits:
      lower = Defined.M_DOUBLE_W  ' see constants at bottom of script
      upper = Defined.M_MAXHEIGHT  ' see constants at bottom of script
      Do While (upper - lower) > Defined.MAXERR AndAlso (n) < Defined.MAXIT ' Repeat until range narrow enough or MAXIT
        m = (upper + lower) / 2
        chl = Math.Sqrt(m) / EllipticK(m)  ' calculate h/L with the test value of m
        If chl > h / L Then  ' compares the calculated h/L with the actual h/L then narrows the range of possible m
          upper = m
        Else
          lower = m
        End If
        n += 1
      Loop
      mult_m.Add(m)

      ' then find the second of two possible solutions for m with the following limits:
      lower = Defined.M_MAXHEIGHT  ' see constants at bottom of script
      upper = 1
      Do While (upper - lower) > Defined.MAXERR AndAlso (n) < Defined.MAXIT ' Repeat until range narrow enough or MAXIT
        m = (upper + lower) / 2
        chl = Math.Sqrt(m) / EllipticK(m)  ' calculate h/L with the test value of m
        If chl < h / L Then  ' compares the calculated h/L with the actual h/L then narrows the range of possible m
          upper = m
        Else
          lower = m
        End If
        n += 1
      Loop

      If m <= Defined.M_MAX Then  ' return this m parameter only if it falls within the maximum useful value (above which the curve breaks down)
        mult_m.Add(m)
      End If

    Else
      ' find the one possible solution for the m parameter
      upper = Defined.M_DOUBLE_W  ' limit the upper end of the search to the maximum value of m for which only one solution exists
      Do While (upper - lower) > Defined.MAXERR AndAlso (n) < Defined.MAXIT ' Repeat until range narrow enough or MAXIT
        m = (upper + lower) / 2
        chl = Math.Sqrt(m) / EllipticK(m)  ' calculate h/L with the test value of m
        If chl > h / L Then  ' compares the calculated h/L with the actual h/L then narrows the range of possible m
          upper = m
        Else
          lower = m
        End If
        n += 1
      Loop
      mult_m.Add(m)
    End If

    Return mult_m
  End Function

  ' Solve for the m parameter from width and height (derived from reference {1} equations (33) and (34) with same notes as above)
  Private Function SolveMFromWidHt(ByVal w As Double, ByVal h As Double) As Double
    Dim n As Integer = 1 ' Iteration counter (quit if >MAXIT)
    Dim lower As Double = 0 ' m must be within this range
    Dim upper As Double = 1
    Dim m As Double
    Dim cwh As Double

    Do While (upper - lower) > Defined.MAXERR AndAlso (n) < Defined.MAXIT ' Repeat until range narrow enough or MAXIT
      m = (upper + lower) / 2
      cwh = (2 * EllipticE(m) - EllipticK(m)) / Math.Sqrt(m)  ' calculate w/h with the test value of m
      If cwh < w / h Then  ' compares the calculated w/h with the actual w/h then narrows the range of possible m
        upper = m
      Else
        lower = m
      End If
      n += 1
    Loop

    Return m
  End Function

  ' Calculate length based on height and an m parameter, derived from reference {1} equation (33), except K(k) should be K(m) and k = sqrt(m)
  Private Function Cal_L(ByVal h As Double, ByVal m As Double) As Double
    Return h * EllipticK(m) / Math.Sqrt(m)
  End Function

  ' Calculate width based on length and an m parameter, derived from reference {1} equation (34), except b = width and K(k) and E(k) should be K(m) and E(m)
  Private Function Cal_W(ByVal L As Double, ByVal m As Double) As Double
    Return L * (2 * EllipticE(m) / EllipticK(m) - 1)
  End Function

  ' Calculate height based on length and an m parameter, from reference {1} equation (33), except K(k) should be K(m) and k = sqrt(m)
  Private Function Cal_H(ByVal L As Double, ByVal m As Double) As Double
    Return L * Math.Sqrt(m) / EllipticK(m)
  End Function

  ' Calculate the unique m parameter based on a start tangent angle, from reference {2}, just above equation (9a), that states k = Sin(angle / 2 + Pi / 4),
  ' but as m = k^2 and due to this script's need for an angle rotated 90° versus the one in reference {1}, the following formula is the result
  ' New note: verified by reference {4}, pg. 78 at the bottom
  Private Function Cal_M(ByVal a As Double) As Double
    Return (1 - Math.Cos(a)) / 2  ' equal to Sin^2(a/2) too
  End Function

  ' Calculate start tangent angle based on an m parameter, derived from above formula
  Private Function Cal_A(ByVal m As Double) As Double
    Return Math.Acos(1 - 2 * m)
  End Function

  ' This is the heart of this script, taking the found (or specified) length, width, and angle values along with the found m parameter to create
  ' a list of points that approximate the shape or form of the elastica. It works by finding the x and y coordinates (which are reversed versus
  ' the original equations (12a) and (12b) from reference {2} due to the 90° difference in orientation) based on the tangent angle along the curve.
  ' See reference {2} for more details on how they derived it. Note that to simplify things, the algorithm only calculates the points for half of the
  ' curve, then mirrors those points along the y-axis.
  Private Function FindBendForm(ByVal L As Double, ByVal w As Double, ByVal m As Double, ByVal ang As Double, ByVal refPln As Plane) As List(Of Point3d)
    L = L / 2  ' because the below algorithm is based on the formulas in reference {2} for only half of the curve
    w = w / 2  ' same

    If ang = 0 Then  ' if angle (and height) = 0, then simply return the start and end points of the straight line
      Dim out As New List(Of Point3d)
      out.Add(refPln.PointAt(w, 0, 0))
      out.Add(refPln.PointAt(-w, 0, 0))
      Return out
    End If

    Dim x As Double
    Dim y As Double
    Dim halfCurvePts As New List(Of Point3d)
    Dim fullCurvePts As New List(Of Point3d)
    Dim translatedPts As New List(Of Point3d)

    ang -= Math.PI / 2  ' a hack to allow this algorithm to work, since the original curve in paper {2} was rotated 90°
    Dim angB As Double = ang + (-Math.PI / 2 - ang) / Defined.CURVEDIVS  ' angB is the 'lowercase theta' which should be in formula {2}(12b) as the interval
    ' start [a typo...see equation(3)]. It's necessary to start angB at ang + [interval] instead of just ang due to integration failing at angB = ang
    halfCurvePts.Add(New Point3d(w, 0, 0))  ' start with this known initial point, as integration will fail when angB = ang

    ' each point {x, y} is calculated from the tangent angle, angB, that occurs at each point (which is why this iterates from ~ang to -pi/2, the known end condition)
    Do While Math.Round(angB, Defined.ROUNDTO) >= Math.Round(-Math.PI / 2, Defined.ROUNDTO)
      y = (Math.Sqrt(2) * Math.Sqrt(Math.Sin(ang) - Math.Sin(angB)) * (w + L)) / (2 * EllipticE(m))  ' note that x and y are swapped vs. (12a) and (12b)
      x = (L / (Math.Sqrt(2) * EllipticK(m))) * Simpson(angB, -Math.PI / 2, 500, ang)  ' calculate the Simpson approximation of the integral (function f below)
      ' over the interval angB ('lowercase theta') to -pi/2. side note: is 500 too few iterations for the Simson algorithm?

      If Math.Round(x, Defined.ROUNDTO) = 0 Then x = 0
      halfCurvePts.Add(New Point3d(x, y, 0))

      angB += (-Math.PI / 2 - ang) / Defined.CURVEDIVS  ' onto the next tangent angle
    Loop

    ' After finding the x and y values for half of the curve, add the {-x, y} values for the rest of the curve
    For Each point As Point3d In halfCurvePts
      If Math.Round(point.X, Defined.ROUNDTO) = 0 Then
        If Math.Round(point.Y, Defined.ROUNDTO) = 0 Then
          fullCurvePts.Add(New Point3d(0, 0, 0))  ' special case when width = 0: when x = 0, only duplicate the point when y = 0 too
        End If
      Else
        fullCurvePts.Add(New Point3d(-point.X, point.Y, 0))
      End If
    Next
    halfCurvePts.Reverse
    fullCurvePts.AddRange(halfCurvePts)

    For Each p As Point3d In fullCurvePts
      translatedPts.Add(refPln.PointAt(p.X, p.Y, p.Z))  ' translate the points from the reference plane to the world plane
    Next

    Return translatedPts
  End Function

  ' Interpolates the points from FindBendForm to create the Elastica curve. Uses start & end tangents for greater accuracy.
  Private Function MakeCurve(ByVal pts As List(Of Point3d), ByVal ang As Double, ByVal refPln As Plane) As Curve
    If ang <> 0 Then
      Dim ts, te As New Vector3d(refPln.XAxis)
      ts.Rotate(ang, refPln.ZAxis)
      te.Rotate(-ang, refPln.ZAxis)
      Return Curve.CreateInterpolatedCurve(pts, 3, CurveKnotStyle.Chord, ts, te)  ' 3rd degree curve with 'Chord' Knot Style
    Else
      Return Curve.CreateInterpolatedCurve(pts, 3)  ' if angle (and height) = 0, then simply interpolate the straight line (no start/end tangents)
    End If
  End Function

  ' Implements the Simpson approximation for an integral of function f below
  Public Function Simpson(a As Double, b As Double, n As Integer, theta As Double) As Double 'n should be an even number
    Dim j As Integer, s1 As Double, s2 As Double, h As Double
    h = (b - a) / n
    s1 = 0
    s2 = 0
    For j = 1 To n - 1 Step 2
      s1 = s1 + fn(a + j * h, theta)
    Next j
    For j = 2 To n - 2 Step 2
      s2 = s2 + fn(a + j * h, theta)
    Next j
    Simpson = h / 3 * (fn(a, theta) + 4 * s1 + 2 * s2 + fn(b, theta))
  End Function

  ' Specific calculation for the above integration
  Public Function fn(x As Double, theta As Double) As Double
    fn = Math.Sin(x) / (Math.Sqrt(Math.Sin(theta) - Math.Sin(x)))  ' from reference {2} formula (12b)
  End Function


  ' Return the Complete Elliptic integral of the 1st kind
  ' Abramowitz and Stegun p.591, formula 17.3.11
  ' Code from http://www.codeproject.com/Articles/566614/Elliptic-integrals
  Public Function EllipticK(ByVal m As Double) As Double
    Dim sum, term, above, below As Double
    sum = 1
    term = 1
    above = 1
    below = 2

    For i As Integer = 1 To 100
      term *= above / below
      sum += Math.Pow(m, i) * Math.Pow(term, 2)
      above += 2
      below += 2
    Next
    sum *= 0.5 * Math.PI
    Return sum
  End Function


  ' Return the Complete Elliptic integral of the 2nd kind
  ' Abramowitz and Stegun p.591, formula 17.3.12
  ' Code from http://www.codeproject.com/Articles/566614/Elliptic-integrals
  Public Function EllipticE(ByVal m As Double) As Double
    Dim sum, term, above, below As Double
    sum = 1
    term = 1
    above = 1
    below = 2

    For i As Integer = 1 To 100
      term *= above / below
      sum -= Math.Pow(m, i) * Math.Pow(term, 2) / above
      above += 2
      below += 2
    Next
    sum *= 0.5 * Math.PI
    Return sum
  End Function

  Friend Partial NotInheritable Class Defined
    Private Sub New()
    End Sub

    ' Note: most of these values for m and h/L ratio were found with Wolfram Alpha and either specific intercepts (x=0) or local minima/maxima. They should be constant.
    Public Const M_SKETCHY As Double = 0.95  ' value of the m parameter where the curvature near the ends of the curve gets wonky
    Public Const M_MAX As Double = 0.993  ' maximum useful value of the m parameter, above which this algorithm for the form of the curve breaks down
    Public Const M_ZERO_W As Double = 0.826114765984970336  ' value of the m parameter when width = 0
    Public Const M_MAXHEIGHT As Double = 0.701327460663101223  ' value of the m parameter at maximum possible height of the bent rod/wire
    Public Const M_DOUBLE_W As Double = 0.180254422335013983  ' minimum value of the m parameter when two width values are possible for a given height and length
    Public Const DOUBLE_W_HL_RATIO As Double = 0.257342117984635757  ' value of the height/length ratio above which there are two possible width values
    Public Const MAX_HL_RATIO As Double = 0.403140189705650243  ' maximum possible value of the height/length ratio

    Public Const MAXERR As Double = 0.0000000001  ' error tolerance
    Public Const MAXIT As Integer = 100  ' maximum number of iterations
    Public Const ROUNDTO As Integer = 10  ' number of decimal places to round off to
    Public Const CURVEDIVS As Integer = 50  ' number of sample points for building the curve (or half-curve as it were)
  End Class
  '</Custom additional code> 
End Class