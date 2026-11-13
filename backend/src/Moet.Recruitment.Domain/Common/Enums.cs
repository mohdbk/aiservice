namespace Moet.Recruitment.Domain;

// Stored as their names (varchar) and guarded by CHECK constraints in the database.

public enum ReviewStatus { Suggested, Approved }

public enum StaffRole { Recruiter, RecruitmentAdmin, Administrator }

public enum TokenPurpose { VerifyEmail, ResetPassword }

public enum JobStatus { Draft, Open, Closed }

public enum EmploymentType { FullTime, PartTime, Contract, Temporary, Internship }

public enum SalaryPeriod { Month, Year }

public enum ApplicationStatus { Open, Replied, Hold, Accepted, Rejected }

public enum ScreeningOutcome { NoQuestions, NoRequirements, Meets, DoesNotMeet }

public enum AnswerType { YesNo, SingleChoice, MultipleChoice, Number, Date, ShortText, LongText }

public enum RequirementRule { MustBe, AtLeast, AtMost }

public enum SendWhen { OnApply, Manual }

public enum CvAnalysisStatus { Pending, Processing, Scored, Failed, Skipped, Waiting }

public enum NoteKind { Note, Correction, System }

public enum InterviewStatus { Scheduled, UnderReview, Cleared, Rejected, Cancelled }

public enum Recommendation { Cleared, Rejected }

public enum OfferStatus { Draft, Sent, Accepted, Declined, Withdrawn }

public enum QuestionType { SingleChoice, MultipleChoice, TrueFalse, ShortAnswer, Number, Essay, FileUpload, Likert }

public enum Difficulty { Easy, Medium, Hard }

public enum AssessmentType { KnowledgeTest, LanguageCheck, SituationalJudgement, WorkSample, Aptitude, Other, Personality }

/// <summary>
/// BigFive and SixteenPF: trait means on the sten scale. WorkplaceBigFive: the Ministry's 25-statement questionnaire, scored as
/// sums (5 to 25) with five bands and role fit. WorkplaceType16: the Ministry's 32-statement questionnaire giving a 4-letter type.
/// </summary>
public enum PersonalityModel { None, BigFive, SixteenPF, WorkplaceBigFive, WorkplaceType16 }

public enum AssessmentStatus { Draft, Published, Archived }

public enum ScorePolicy { Highest, Latest }

public enum InvitationStatus { Invited, InProgress, Submitted, Marked, Expired, Cancelled }

public enum InvitationResult { Pass, Fail, AwaitingMarking, Completed }

public enum AttemptStatus { InProgress, Submitted, Marked }

public enum EmailStatus { Queued, Sent, Failed }

public enum BackgroundJobStatus { Queued, Running, Done, Failed }

/// <summary>Record changes (Created, Updated, Deleted) and other actions people take (Action: sign-ins, downloads, sending, views of personal data).</summary>
public enum AuditAction { Created, Updated, Deleted, Action }

public enum ActorType { Staff, Applicant, System }

public enum ProposalStatus { Pending, Approved, Cancelled, Failed }
