' ============================================================
' CÁCH CÀI (làm 1 lần, 2 phút) - THAY cho đoạn macro cũ
' 1) Mở file DK_KET_HON.xlsx -> bấm Alt + F11
' 2) Cột trái (Project) bấm đúp "ThisWorkbook"
' 3) Nếu khung bên phải đang có code cũ: bấm vào khung, Ctrl+A, Delete (xóa hết)
' 4) Dán NGUYÊN đoạn code bên dưới (từ dòng "Option Explicit") vào khung trắng
' 5) Đóng cửa sổ VBA -> File > Save As -> chọn loại "Excel Macro-Enabled Workbook (*.xlsm)"
' 6) Lần sau mở file, bấm "Enable Content" (Bật nội dung).
'
' CÁCH DÙNG: mở sheet của năm cần in. Ở khung xanh trên cùng:
'   - Ô "Cách chọn": Tất cả hồ sơ / Từ số - đến số / Từng số
'   - Điền số vào ô tương ứng (ô không dùng sẽ mờ đi). Chọn xong là bảng tự lọc ngay.
'   - Ô "Chuyển sang năm khác": chọn năm là nhảy sang sheet đó.
'   - Bấm chữ "XEM TRƯỚC KHI IN" để xem rồi mới in (mỗi bảng 1 trang).
' Macro cũ (bấm chữ xanh ở cột D sheet ML để đẩy sang FORM) vẫn giữ nguyên bên dưới.
' ============================================================
Option Explicit

Private Const FIRST_ROW As Long = 8   ' dong dau cua bang thu 1
Private Const BLOCK As Long = 9       ' moi bang chiem 9 dong

Private Function IsBangIn(ByVal Sh As Object) As Boolean
    On Error Resume Next
    IsBangIn = (CStr(Sh.Range("K1").Value) = "BANG_IN_HANG_LOAT")
End Function

' Khi mo file: dam bao su kien luon bat (phong khi lan truoc bi dung giua chung)
Private Sub Workbook_Open()
    Application.EnableEvents = True
End Sub

' Moi lan vao sheet nam: tat duong ke ngat trang (cac duong nay lam Excel cham khi nhieu bang)
Private Sub Workbook_SheetActivate(ByVal Sh As Object)
    If IsBangIn(Sh) Then
        On Error Resume Next
        Sh.DisplayPageBreaks = False
    End If
End Sub

Private Sub Workbook_SheetFollowHyperlink(ByVal Sh As Object, ByVal Target As Hyperlink)
    Dim f As Worksheet
    If Sh.Name = "ML" Then
        ' ---- macro cu: bam chu xanh cot D -> FORM ----
        If Target.Range.Column <> 4 Then Exit Sub
        Set f = ThisWorkbook.Worksheets("FORM")
        f.Range("J2").Value = CStr(Sh.Cells(Target.Range.Row, 2).Value)
        f.Activate
        Exit Sub
    End If
    If IsBangIn(Sh) Then
        ' ---- nut "XEM TRUOC KHI IN" (o E3) ----
        If Target.Range.Row = 3 And Target.Range.Column = 5 Then
            Sh.Activate
            Application.ScreenUpdating = True
            Sh.PrintPreview
            On Error Resume Next
            Sh.DisplayPageBreaks = False
        End If
    End If
End Sub

Private Sub Workbook_SheetChange(ByVal Sh As Object, ByVal Target As Range)
    If Not IsBangIn(Sh) Then Exit Sub
    ' ---- chon nam khac (o E5) ----
    If Not Intersect(Target, Sh.Range("E5")) Is Nothing Then
        NhayNam Sh
        Exit Sub
    End If
    ' ---- doi cach chon / nhap so (A3, A5, C5, D5) ----
    If Intersect(Target, Sh.Range("A3,A5,C5,D5")) Is Nothing Then Exit Sub
    LocBangIn Sh
End Sub

Private Sub NhayNam(ByVal Sh As Object)
    Dim nm As String
    nm = CStr(Sh.Range("E5").Value)
    On Error GoTo Fin
    Application.EnableEvents = False
    Sh.Range("E5").Value = Sh.Name            ' tra o nay ve ten sheet cua no
    If nm <> "" And nm <> Sh.Name Then ThisWorkbook.Worksheets(nm).Activate
Fin:
    Application.EnableEvents = True
End Sub

Private Sub LocBangIn(ByVal ws As Worksheet)
    Dim lastRow As Long, n As Long, k As Long, i As Long, j As Long, r As Long, total As Long
    Dim arr As Variant, t As String
    Dim num() As Double, show() As Boolean, flags() As Variant
    Dim mode As Variant, lo As Double, hi As Double, tmp As Double
    Dim loA() As Double, hiA() As Double, m As Long
    Dim sgl() As Double, ns As Long, found As Boolean
    Dim txt As String, parts() As String, p As String, pos As Long
    Dim shown As Long, msg As String, notFound As String, v As Variant
    Dim calcOld As Long, okExit As Boolean

    calcOld = Application.Calculation
    On Error GoTo Fin
    Application.EnableEvents = False
    Application.ScreenUpdating = False
    Application.Calculation = xlCalculationManual
    Application.Cursor = xlWait

    lastRow = ws.Cells(ws.Rows.Count, "D").End(xlUp).Row
    If lastRow < FIRST_ROW + 3 Then GoTo Fin
    n = (lastRow - (FIRST_ROW + 3)) \ BLOCK + 1
    total = BLOCK * n

    ReDim num(1 To n): ReDim show(1 To n)
    arr = ws.Range("D" & FIRST_ROW & ":D" & lastRow).Value2
    For k = 1 To n
        t = CStr(arr(BLOCK * (k - 1) + 2, 1))
        num(k) = Val(Mid(t, InStrRev(t, ":") + 1))
    Next k

    mode = Application.Match(ws.Range("A3").Value, ws.Range("K2:K4"), 0)
    If IsError(mode) Then mode = 1

    Select Case mode
    Case 1
        For k = 1 To n: show(k) = True: Next k

    Case 2
        v = ws.Range("A5").Value
        If IsEmpty(v) Or Not IsNumeric(v) Or IsEmpty(ws.Range("C5").Value) Or Not IsNumeric(ws.Range("C5").Value) Then
            ws.Range("K9").Value = ws.Range("K10").Value
            okExit = True: GoTo Fin
        End If
        lo = CDbl(ws.Range("A5").Value): hi = CDbl(ws.Range("C5").Value)
        If lo > hi Then tmp = lo: lo = hi: hi = tmp
        For k = 1 To n
            show(k) = (num(k) >= lo And num(k) <= hi)
        Next k

    Case 3
        txt = CStr(ws.Range("D5").Value)
        txt = Replace(txt, ChrW(8211), "-"): txt = Replace(txt, ChrW(8212), "-")
        txt = Replace(txt, ";", ",")
        Do While InStr(txt, " -") > 0: txt = Replace(txt, " -", "-"): Loop
        Do While InStr(txt, "- ") > 0: txt = Replace(txt, "- ", "-"): Loop
        txt = Replace(txt, " ", ",")
        parts = Split(txt, ",")
        ReDim loA(0 To UBound(parts)): ReDim hiA(0 To UBound(parts)): ReDim sgl(0 To UBound(parts))
        For i = 0 To UBound(parts)
            p = parts(i)
            If p Like "*#*" Then
                pos = InStr(p, "-")
                If pos > 1 Then
                    loA(m) = Val(Left(p, pos - 1)): hiA(m) = Val(Mid(p, pos + 1))
                    If loA(m) > hiA(m) Then tmp = loA(m): loA(m) = hiA(m): hiA(m) = tmp
                Else
                    loA(m) = Val(p): hiA(m) = loA(m)
                    sgl(ns) = loA(m): ns = ns + 1
                End If
                m = m + 1
            End If
        Next i
        If m = 0 Then
            ws.Range("K9").Value = ws.Range("K11").Value
            okExit = True: GoTo Fin
        End If
        For k = 1 To n
            For j = 0 To m - 1
                If num(k) >= loA(j) And num(k) <= hiA(j) Then show(k) = True: Exit For
            Next j
        Next k
        For i = 0 To ns - 1
            found = False
            For k = 1 To n
                If num(k) = sgl(i) Then found = True: Exit For
            Next k
            If Not found Then notFound = notFound & IIf(notFound = "", "", ", ") & CStr(sgl(i))
        Next i
    End Select

    For k = 1 To n
        If show(k) Then shown = shown + 1
    Next k
    If notFound <> "" Then msg = ws.Range("K12").Value & notFound
    If shown = 0 Then
        msg = ws.Range("K13").Value & IIf(notFound <> "", ws.Range("K12").Value & notFound, "")
        For k = 1 To n: show(k) = True: Next k
        shown = n
    End If

    ' ---- loc bang bang AutoFilter: ghi co 1/0 cho tung dong roi loc 1 lan (rat nhanh) ----
    ws.AutoFilterMode = False
    If shown < n Then
        ReDim flags(1 To total, 1 To 1)
        r = 0
        For k = 1 To n
            For j = 1 To BLOCK
                r = r + 1
                If show(k) Then flags(r, 1) = 1 Else flags(r, 1) = 0
            Next j
        Next k
        ws.Range("L" & FIRST_ROW).Resize(total, 1).Value = flags
        ws.Range("L" & (FIRST_ROW - 1)).Resize(total + 1, 1).AutoFilter Field:=1, Criteria1:="1"
    End If

    ws.Range("K6").Value = shown
    ws.Range("K9").Value = msg
    okExit = True

Fin:
    If Err.Number <> 0 And Not okExit Then
        ws.Range("K9").Value = "Co loi: " & Err.Description & " - hay chon lai."
    End If
    On Error Resume Next
    Application.Calculation = calcOld
    Application.Cursor = xlDefault
    Application.ScreenUpdating = True
    Application.EnableEvents = True
    If ws Is ActiveSheet Then ActiveWindow.ScrollRow = FIRST_ROW
End Sub
' ============================================================
' STP SELF-TEST - engine gọi để kiểm tra macro biên dịch được.
' Không thay đổi dữ liệu hồ sơ.
' ============================================================
Public Sub StpSelfTest()
    Dim workbookName As String
    workbookName = ThisWorkbook.Name
End Sub
