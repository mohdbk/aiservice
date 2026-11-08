frappe.ui.form.on("Assessment Attempt", {
	refresh(frm) {
		if (frm.doc.status === "In Progress") return;
		frm.add_custom_button(__("Save Marks and Recalculate"), () => {
			const run = () => frappe.call({
				method: "moet_careers.assessments.save_marks",
				args: { attempt: frm.doc.name },
				callback: (r) => {
					const left = (r.message.still_to_mark || []).length;
					frappe.show_alert({
						message: left ? __("Saved. {0} answer(s) still need marking.", [left]) : __("Marked: {0}%", [r.message.percent]),
						indicator: left ? "orange" : "green",
					});
					frm.reload_doc();
				},
			});
			frm.is_dirty() ? frm.save().then(run) : run();
		});
		if ((frm.doc.marks || []).some((m) => m.needs_marking)) {
			frm.add_custom_button(__("Suggest Marks"), () => frappe.call({
				method: "moet_careers.assessments.suggest_marks_from_desk",
				args: { attempt: frm.doc.name },
				freeze: true,
				callback: () => frm.reload_doc(),
			}));
		}
		frm.set_intro(__("Enter points in Marker's Points for answers that need marking, then Save Marks and Recalculate. Suggested points are guidance only."));
	},
});
