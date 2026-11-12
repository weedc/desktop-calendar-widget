"""The same eight resize targets work for normal and desktop-level windows."""
from PySide6.QtCore import Qt, QRect
from PySide6.QtWidgets import QWidget


def calculate(original, edges, delta, minimum):
    left, top = original.x(), original.y()
    right, bottom = left + original.width(), top + original.height()
    if edges & Qt.Edge.LeftEdge: left = min(right - minimum.width(), left + delta.x())
    if edges & Qt.Edge.RightEdge: right = max(left + minimum.width(), right + delta.x())
    if edges & Qt.Edge.TopEdge: top = min(bottom - minimum.height(), top + delta.y())
    if edges & Qt.Edge.BottomEdge: bottom = max(top + minimum.height(), bottom + delta.y())
    return QRect(left, top, right - left, bottom - top)


class ResizeHandle(QWidget):
    def __init__(self, owner, edges):
        super().__init__(owner)
        self.owner = owner; self.edges = edges; self.drag = None
        self.setStyleSheet('background:transparent;'); self.setAccessibleName('크기 조절 ' + str(edges))
        left, top, right, bottom = Qt.Edge.LeftEdge, Qt.Edge.TopEdge, Qt.Edge.RightEdge, Qt.Edge.BottomEdge
        cursor = Qt.CursorShape.SizeHorCursor if edges in (left, right) else Qt.CursorShape.SizeVerCursor if edges in (top, bottom) else Qt.CursorShape.SizeFDiagCursor if edges in (left | top, right | bottom) else Qt.CursorShape.SizeBDiagCursor
        self.setCursor(cursor)

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self.drag = (event.globalPosition().toPoint(), self.owner.geometry())
            self.grabMouse(); event.accept()

    def mouseMoveEvent(self, event):
        if self.drag:
            start, original = self.drag
            self.owner.setGeometry(calculate(original, self.edges, event.globalPosition().toPoint() - start, self.owner.minimumSize()))

    def mouseReleaseEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton and self.drag:
            self.drag = None; self.releaseMouse()
            if not self.owner.demo: self.owner.save_geometry()


class ResizeHandles:
    def __init__(self, owner):
        left, top, right, bottom = Qt.Edge.LeftEdge, Qt.Edge.TopEdge, Qt.Edge.RightEdge, Qt.Edge.BottomEdge
        self.owner = owner
        self.handles = [ResizeHandle(owner, e) for e in (left, top, right, bottom, left | top, right | top, left | bottom, right | bottom)]

    def layout(self):
        w, h, c, b = self.owner.width(), self.owner.height(), 18, 7
        rectangles = [(0, c, b, h - 2*c), (c, 0, w - 2*c, b), (w - b, c, b, h - 2*c), (c, h - b, w - 2*c, b),
                      (0, 0, c, c), (w - c, 0, c, c), (0, h - c, c, c), (w - c, h - c, c, c)]
        for handle, rectangle in zip(self.handles, rectangles): handle.setGeometry(*rectangle); handle.raise_()
