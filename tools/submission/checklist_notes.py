# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Numbered endnotes and reciprocal links for an unchanged official checklist."""
import io
from xml.sax.saxutils import escape

from pypdf import PdfReader
from pypdf.annotations import Link
from pypdf.generic import (ArrayObject, DecodedStreamObject, DictionaryObject,
                          FloatObject, NameObject, NumberObject, TextStringObject)
from pypdf.generic import Fit
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import Flowable, KeepTogether, Paragraph, SimpleDocTemplate, Spacer


def notes_document(title, author, rows, identity, draft, signing_copy=False, declared_gaps=False):
    positions = {}

    class NoteAnchor(Flowable):
        width = 0
        height = 0

        def __init__(self, number):
            super().__init__()
            self.number = number

        def draw(self):
            x, y = self.canv.absolutePosition(0, 0)
            positions[self.number] = (self.canv.getPageNumber()-1, x, y)

    body = ParagraphStyle('body', fontName='Helvetica', fontSize=9, leading=12, spaceAfter=8)
    heading = ParagraphStyle('heading', parent=body, fontName='Helvetica-Bold', fontSize=16, leading=20)
    subheading = ParagraphStyle('subheading', parent=body, fontName='Helvetica-Bold', spaceAfter=5)
    small = ParagraphStyle('small', parent=body, fontSize=7, leading=9, spaceAfter=3)

    def paragraph(value, style=body):
        return Paragraph(escape(str(value)).replace('\n', '<br/>'), style)

    story = [paragraph('Checklist notes', heading), paragraph(title),
             paragraph('Prepared for '+author+'. '+('Signature and date remain blank. ' if not signing_copy else '')+
                       'Numbered references beside the preceding checklist link to these notes. Each note heading links back to its checklist item.'),
             paragraph('These notes record our interpretation of the published requirements. They do not imply or predict Crestron acceptance, publication or certification.')]
    if draft:
        story.append(paragraph('Draft: no requirements have been attested and all checkboxes remain blank.'))
    elif not signing_copy:
        story.append(paragraph('UNSIGNED REVIEW - NOT FOR SUBMISSION', subheading))
    if declared_gaps:
        story.append(paragraph('Disclosed limitations remain identified below. An unchecked item with a numbered note is not represented as a full pass.'))
    statuses = {'Passed':'Checked', 'NotApplicable':'Not applicable',
                'NotTested':'Not evaluated', 'GapDeclared':'Unchecked - see qualification'}
    for number, row in enumerate(rows, 1):
        story.append(KeepTogether([NoteAnchor(number), paragraph(f'{number}. {row["label"]}', subheading),
                                  paragraph('Checked - reviewed interpretation' if row['state']=='Passed' and row.get('interpretationReviewed') else statuses[row['state']]),
                                  paragraph(row['rationale'] or 'The mapped evidence supports the recorded checklist result.'),
                                  Spacer(1, 6)]))
    story.append(paragraph('Document identification', subheading))
    for key, value in identity.items():
        story.append(paragraph(key+': '+str(value), small))
    buffer = io.BytesIO()

    def footer(canvas, document):
        canvas.setFont('Helvetica', 8)
        canvas.drawRightString(558, 24, f'Checklist notes - {document.page}')

    SimpleDocTemplate(buffer, pagesize=(612,792), leftMargin=54, rightMargin=54,
                      topMargin=40, bottomMargin=40, title=title, author=author).build(
                          story, onFirstPage=footer, onLaterPages=footer)
    return PdfReader(buffer), positions


def link_notes(writer, rows, positions, notes_start):
    by_field = {row['field']:(number,row) for number,row in enumerate(rows,1)}
    font = DictionaryObject({NameObject('/Type'):NameObject('/Font'),
                             NameObject('/Subtype'):NameObject('/Type1'),
                             NameObject('/BaseFont'):NameObject('/Helvetica')})
    linked = set()
    for index in range(notes_start):
        page = writer.pages[index]
        for reference in list(page.get('/Annots', [])):
            widget = reference.get_object()
            if widget.get('/Subtype')!='/Widget' or widget.get('/T') not in by_field:
                continue
            number,row = by_field[widget['/T']]
            left,bottom,_,top = map(float,widget['/Rect'])
            width,height = 28,12
            x,y = left-width-3,(bottom+top-height)/2
            if x<float(page.mediabox.left):
                raise ValueError('No space for a numbered checklist reference')
            label = f'N/A {number}' if row['state']=='NotApplicable' else f'[{number}]'
            appearance = DecodedStreamObject()
            appearance.update({NameObject('/Type'):NameObject('/XObject'),
                NameObject('/Subtype'):NameObject('/Form'),
                NameObject('/BBox'):ArrayObject(map(FloatObject,[0,0,width,height])),
                NameObject('/Resources'):DictionaryObject({NameObject('/Font'):DictionaryObject({NameObject('/Helv'):font})})})
            appearance.set_data(f'q BT /Helv 7 Tf 0 g 0 3 Td ({label}) Tj ET Q\n'.encode('ascii'))
            rectangle = [x,y,x+width,y+height]
            writer.add_annotation(index, DictionaryObject({NameObject('/Type'):NameObject('/Annot'),
                NameObject('/Subtype'):NameObject('/FreeText'),NameObject('/Rect'):ArrayObject(map(FloatObject,rectangle)),
                NameObject('/Contents'):TextStringObject(label),NameObject('/NM'):TextStringObject('submission-note:'+widget['/T']),
                NameObject('/DA'):TextStringObject('/Helv 7 Tf 0 g'),NameObject('/F'):NumberObject(4),
                NameObject('/AP'):DictionaryObject({NameObject('/N'):writer._add_object(appearance)})}))
            note_page,note_x,note_y = positions[number]
            destination = notes_start+note_page
            forward = writer.add_annotation(index,Link(rect=rectangle, target_page_index=destination,
                                                       fit=Fit.xyz(left=note_x,top=note_y+12,zoom=0)))
            backward = writer.add_annotation(destination,Link(rect=[note_x,note_y-24,558,note_y+3],target_page_index=index,
                                                              fit=Fit.xyz(left=0,top=top+24,zoom=0)))
            # Local explicit destinations identify page objects, not numeric page indices.
            # Resolve these ourselves because pypdf versions differ in Link serialization.
            forward['/Dest'][0] = writer.pages[destination].indirect_reference
            backward['/Dest'][0] = writer.pages[index].indirect_reference
            linked.add(number)
    if linked!=set(positions):
        raise ValueError('Numbered notes do not match the official checklist widgets')
